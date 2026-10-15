using System.ComponentModel;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace SparseTypes.Blazor;

/// <summary>A stable-identity row supplied to <see cref="SparseCollection{TModel, TElement}"/>.</summary>
/// <typeparam name="TModel">The root model type.</typeparam>
/// <typeparam name="TElement">The collection element model type.</typeparam>
public sealed class SparseCollectionRow<TModel, TElement>
    where TModel : class
    where TElement : class
{
    internal delegate bool RemoveOperation(
        SparseCollectionRow<TModel, TElement> row,
        out SparseCollectionEditError error
    );
    internal delegate bool MoveOperation(
        SparseCollectionRow<TModel, TElement> row,
        int newIndex,
        out SparseCollectionEditError error
    );

    private readonly RemoveOperation _remove;
    private readonly MoveOperation _move;

    internal SparseCollectionRow(
        TElement item,
        int index,
        Guid uiKey,
        SparsePath<TModel, TElement> path,
        SparseCollectionItemState? itemState,
        string? stateQueryError,
        RemoveOperation remove,
        MoveOperation move
    )
    {
        Item = item;
        Index = index;
        UiKey = uiKey;
        Path = path;
        ItemState = itemState;
        StateQueryError = stateQueryError;
        _remove = remove;
        _move = move;
    }

    /// <summary>Gets the live collection element model.</summary>
    public TElement Item { get; }

    /// <summary>Gets the row's current index when the collection was rendered.</summary>
    public int Index { get; }

    /// <summary>Gets the UI identity, which remains stable for a keyed row across model replacement.</summary>
    public Guid UiKey { get; }

    /// <summary>Gets the typed, root-preserving path to this row.</summary>
    public SparsePath<TModel, TElement> Path { get; }

    /// <summary>Gets the row's semantic state snapshot, when the collection supports item state.</summary>
    /// <remarks>Item state is available for keyed collections; unkeyed collections expose positional paths only.</remarks>
    public SparseCollectionItemState? ItemState { get; }

    /// <summary>Gets why the item state snapshot was unavailable, or null when it was available.</summary>
    public string? StateQueryError { get; }

    /// <summary>Gets the item state snapshot or throws when the collection cannot provide one.</summary>
    /// <exception cref="InvalidOperationException">The item state query was unavailable.</exception>
    public SparseCollectionItemState RequireItemState() =>
        ItemState
        ?? throw new InvalidOperationException(
            StateQueryError ?? "The collection item state is unavailable."
        );

    /// <summary>Navigates from this row to a child member with a root-preserving typed path.</summary>
    /// <param name="selector">A member-access chain from the row model.</param>
    /// <typeparam name="TValue">The addressed member type.</typeparam>
    /// <returns>A typed path usable by <see cref="SparseField{TModel, TValue}"/>.</returns>
    public SparsePath<TModel, TValue> Select<TValue>(Expression<Func<TElement, TValue>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new SparsePath<TModel, TValue>(
            SparseFieldContext<TModel, object?>.AppendSelector(
                Path.Path,
                typeof(TElement),
                selector,
                nameof(selector)
            )
        );
    }

    /// <summary>Removes this row if the same model instance is still present.</summary>
    public bool TryRemove(out SparseCollectionEditError error) => _remove(this, out error);

    /// <summary>Moves this row to the requested index if it is still present.</summary>
    /// <param name="newIndex">The target index.</param>
    /// <param name="error">The failure reason, or <see cref="SparseCollectionEditError.None"/> on success.</param>
    /// <returns>True when the row moved.</returns>
    public bool TryMoveTo(int newIndex, out SparseCollectionEditError error) =>
        _move(this, newIndex, out error);
}

/// <summary>Enumerates a session collection and assigns stable Blazor keys to its typed row template.</summary>
/// <typeparam name="TModel">The root edit-session model type.</typeparam>
/// <typeparam name="TElement">The collection element model type.</typeparam>
public sealed class SparseCollection<TModel, TElement> : ComponentBase, IDisposable
    where TModel : class
    where TElement : class
{
    private sealed class KeyedRowHost : ComponentBase
    {
        [Parameter]
        public RenderFragment? ChildContent { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder) =>
            builder.AddContent(0, ChildContent);
    }

    private readonly Dictionary<TElement, Guid> _identityByItem = new(
        ReferenceEqualityComparer.Instance
    );
    private readonly HashSet<TElement> _itemsInRender = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SparsePathSegment, Guid> _identityByPath = [];
    private readonly HashSet<SparsePathSegment> _pathsInRender = [];
    private readonly Dictionary<Guid, Guid> _identityByTemporaryKey = [];
    private readonly HashSet<Guid> _temporaryKeysInRender = [];
    private ISparseEditScope<TModel>? _scope;
    private INotifyPropertyChanged? _notifier;
    private SparseCollectionEditor<TElement>? _editor;
    private SparsePath _path;
    private bool _hasPath;
    private bool _disposed;
    private bool _mutationActive;
    private bool _notificationDuringMutation;
    private bool _parameterRefreshDuringMutation;
    private bool _skipNextParameterRefresh;
    private long _rowsVersion = 1;
    private long _builtRowsVersion;
    private long _bindingVersion;
    private long _parameterSetVersion;
    private int _refreshQueued;
    private SparseCollectionState? _collectionState;

    /// <summary>Gets the current typed rows, refreshed for each render.</summary>
    public IReadOnlyList<SparseCollectionRow<TModel, TElement>> Rows { get; private set; } = [];

    /// <summary>Gets the latest collection state snapshot, when the session provides one.</summary>
    public SparseCollectionState? CollectionState => _collectionState;

    /// <summary>Gets the removed entries from the latest keyed collection state.</summary>
    /// <exception cref="InvalidOperationException">A removed entry does not contain its typed baseline value.</exception>
    public IReadOnlyList<TElement> RemovedItems
    {
        get
        {
            if (_collectionState is not { } state)
            {
                return [];
            }

            var removed = new List<TElement>();
            foreach (var change in state.ChangedItems)
            {
                if (!change.IsRemoved)
                {
                    continue;
                }

                if (!change.Before.IsPresent || change.Before.Value is not TElement item)
                {
                    throw new InvalidOperationException(
                        $"The removed entry at '{state.Path}' has no baseline value of type '{typeof(TElement)}'."
                    );
                }

                removed.Add(item);
            }

            return removed;
        }
    }

    /// <summary>Gets the latest collection enumeration error, or null when successful.</summary>
    public string? QueryError { get; private set; }

    /// <summary>Gets the generated collection editor for this path.</summary>
    public SparseCollectionEditor<TElement> Editor =>
        _editor ?? throw new InvalidOperationException("The collection has not been bound.");

    /// <summary>Appends an element through the session collection editor.</summary>
    /// <param name="item">The element model to append.</param>
    /// <param name="error">The failure reason, or <see cref="SparseCollectionEditError.None"/> on success.</param>
    /// <returns>True when the item was appended.</returns>
    public bool TryAdd(TElement item, out SparseCollectionEditError error)
    {
        ArgumentNullException.ThrowIfNull(item);
        BeginMutation();
        bool added;
        try
        {
            added = Editor.TryAdd(item, out error);
        }
        finally
        {
            _mutationActive = false;
        }

        if (added || _notificationDuringMutation || _parameterRefreshDuringMutation)
        {
            if (!_notificationDuringMutation)
            {
                InvalidateRows();
            }

            RefreshRows();
            _skipNextParameterRefresh =
                _notificationDuringMutation && !_parameterRefreshDuringMutation;
        }

        if (added)
        {
            _ = InvokeAsync(StateHasChanged);
        }

        return added;
    }

    /// <summary>Gets or sets the root collection path.</summary>
    [Parameter]
    public SparsePath Path { get; set; }

    /// <summary>Gets or sets the typed fragment rendered once per row.</summary>
    [Parameter]
    public RenderFragment<SparseCollectionRow<TModel, TElement>>? RowTemplate { get; set; }

    /// <summary>Gets or sets the content rendered when no rows are available.</summary>
    [Parameter]
    public RenderFragment? EmptyContent { get; set; }

    /// <summary>Gets or sets content rendered when collection enumeration fails.</summary>
    [Parameter]
    public RenderFragment<string>? ErrorContent { get; set; }

    /// <summary>Gets or sets the nearest edit scope for this model type.</summary>
    [CascadingParameter]
    public ISparseEditScope<TModel>? Scope { get; set; }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        Interlocked.Increment(ref _parameterSetVersion);
        var scope =
            Scope
            ?? throw new InvalidOperationException(
                "SparseCollection must be placed inside a SparseEdit for the same model type."
            );
        if (Path.RootType != typeof(TModel) || Path.Segments.Count != 1)
        {
            throw new InvalidOperationException(
                "SparseCollection requires a single-member path rooted at its model type."
            );
        }

        if (!ReferenceEquals(_scope, scope) || !_hasPath || !_path.Equals(Path))
        {
            Unsubscribe();
            _scope = scope;
            _path = Path;
            _hasPath = true;
            Interlocked.Increment(ref _bindingVersion);
            InvalidateRows();
            _editor = SparseSessionInterop.CreateCollectionEditor<TElement>(scope.Session, Path);
            if (scope.Session is INotifyPropertyChanged notifier)
            {
                _notifier = notifier;
                notifier.PropertyChanged += OnSessionChanged;
            }

            _skipNextParameterRefresh = false;
        }
        else if (_mutationActive)
        {
            _parameterRefreshDuringMutation = true;
            return;
        }
        else if (_skipNextParameterRefresh)
        {
            _skipNextParameterRefresh = false;
        }
        else
        {
            InvalidateRows();
        }

        RefreshRows();
    }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var queryError = QueryError;
        if (queryError is not null)
        {
            if (ErrorContent is not null)
            {
                builder.AddContent(0, ErrorContent(queryError));
            }

            return;
        }

        var rowTemplate = RowTemplate;
        if (Rows.Count == 0)
        {
            if (EmptyContent is not null)
            {
                builder.AddContent(0, EmptyContent);
            }

            return;
        }

        if (rowTemplate is null)
        {
            return;
        }

        for (var index = 0; index < Rows.Count; index++)
        {
            var row = Rows[index];
            builder.OpenComponent<KeyedRowHost>(0);
            builder.SetKey(row.UiKey);
            builder.AddAttribute(1, nameof(KeyedRowHost.ChildContent), rowTemplate(row));
            builder.CloseComponent();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unsubscribe();
    }

    private void RefreshRows()
    {
        var version = Interlocked.Read(ref _rowsVersion);
        if (_builtRowsVersion == version)
        {
            return;
        }

        RebuildRows();
        _builtRowsVersion = version;
    }

    private void RebuildRows()
    {
        var editor = _editor;
        if (editor is null)
        {
            Rows = [];
            QueryError = "The collection editor is unavailable.";
            return;
        }

        _itemsInRender.Clear();
        _pathsInRender.Clear();
        _temporaryKeysInRender.Clear();
        var count = editor.Count;
        var rows = new List<SparseCollectionRow<TModel, TElement>>(count);
        SparseCollectionState? collectionState = null;
        var hasCollectionState = false;
        string? stateQueryError = null;
        try
        {
            hasCollectionState = SparseSessionInterop.TryCollectionState(
                _scope!.Session,
                _path,
                out collectionState
            );
        }
        catch (Exception exception)
        {
            stateQueryError = exception.Message;
        }

        _collectionState = hasCollectionState ? collectionState : null;

        var bindingVersion = Interlocked.Read(ref _bindingVersion);
        for (var index = 0; index < count; index++)
        {
            if (!editor.TryGetAt(index, out var item, out var error) || item is null)
            {
                Rows = [];
                QueryError = $"Could not read collection row {index}: {error}.";
                return;
            }

            _itemsInRender.Add(item);
            if (
                !SparseSessionInterop.TryGetCollectionRowIdentity(
                    _scope!.Session,
                    _path,
                    index,
                    out var identitySegment,
                    out var key,
                    out var temporaryKey,
                    out var isKeyed,
                    out var identityError
                )
            )
            {
                Rows = [];
                QueryError = $"Could not identify collection row {index}: {identityError}";
                return;
            }

            var uiKey = GetUiKey(item, identitySegment, temporaryKey, isKeyed);
            SparseCollectionItemState? itemState = null;
            var rowStateError = stateQueryError;
            if (hasCollectionState && collectionState is { } state)
            {
                if (isKeyed && identitySegment.Kind == SparsePathSegmentKind.TemporaryKey)
                {
                    itemState = state.GetTemporaryState(temporaryKey!.Value);
                }
                else if (isKeyed)
                {
                    itemState = state.GetItemState(key);
                }
            }
            else if (rowStateError is null)
            {
                rowStateError = isKeyed
                    ? "The session does not provide collection item state for this keyed path."
                    : "Semantic item state is available only for keyed collections.";
            }

            rows.Add(
                new SparseCollectionRow<TModel, TElement>(
                    item,
                    index,
                    uiKey,
                    new SparsePath<TModel, TElement>(_path.Append(identitySegment)),
                    itemState,
                    rowStateError,
                    (
                        SparseCollectionRow<TModel, TElement> row,
                        out SparseCollectionEditError error
                    ) => RemoveRow(row, bindingVersion, out error),
                    (
                        SparseCollectionRow<TModel, TElement> row,
                        int newIndex,
                        out SparseCollectionEditError error
                    ) => MoveRow(row, newIndex, bindingVersion, out error)
                )
            );
        }

        foreach (
            var item in _identityByItem.Keys.Where(item => !_itemsInRender.Contains(item)).ToArray()
        )
        {
            _identityByItem.Remove(item);
        }

        foreach (
            var path in _identityByPath.Keys.Where(path => !_pathsInRender.Contains(path)).ToArray()
        )
        {
            _identityByPath.Remove(path);
        }

        foreach (
            var temporaryKey in _identityByTemporaryKey
                .Keys.Where(key => !_temporaryKeysInRender.Contains(key))
                .ToArray()
        )
        {
            _identityByTemporaryKey.Remove(temporaryKey);
        }

        Rows = rows;
        QueryError = null;
    }

    private Guid GetUiKey(
        TElement item,
        SparsePathSegment identity,
        Guid? temporaryKey,
        bool isKeyed
    )
    {
        if (!isKeyed)
        {
            if (!_identityByItem.TryGetValue(item, out var itemIdentity))
            {
                itemIdentity = Guid.NewGuid();
                _identityByItem.Add(item, itemIdentity);
            }

            return itemIdentity;
        }

        _pathsInRender.Add(identity);
        if (temporaryKey is { } temporary)
        {
            _temporaryKeysInRender.Add(temporary);
        }

        var hasIdentity = _identityByPath.TryGetValue(identity, out var uiKey);
        if (!hasIdentity && temporaryKey is { } temporaryIdentity)
        {
            hasIdentity = _identityByTemporaryKey.TryGetValue(temporaryIdentity, out uiKey);
        }

        if (!hasIdentity)
        {
            uiKey = Guid.NewGuid();
        }

        _identityByPath[identity] = uiKey;
        if (temporaryKey is { } stableTemporaryKey)
        {
            _identityByTemporaryKey[stableTemporaryKey] = uiKey;
        }

        return uiKey;
    }

    private bool RemoveRow(
        SparseCollectionRow<TModel, TElement> row,
        long bindingVersion,
        out SparseCollectionEditError error
    )
    {
        if (_disposed || bindingVersion != Interlocked.Read(ref _bindingVersion))
        {
            error = SparseCollectionEditError.UnknownCollection;
            return false;
        }

        var editor = _editor;
        if (editor is null)
        {
            error = SparseCollectionEditError.UnknownCollection;
            return false;
        }

        var index = FindCurrentIndex(editor, row.UiKey);
        if (index < 0)
        {
            error = SparseCollectionEditError.IndexOutOfRange;
            return false;
        }

        BeginMutation();
        bool removed;
        try
        {
            removed = editor.TryRemoveAt(index, out error);
        }
        finally
        {
            _mutationActive = false;
        }

        if (removed || _notificationDuringMutation || _parameterRefreshDuringMutation)
        {
            if (!_notificationDuringMutation)
            {
                InvalidateRows();
            }

            RefreshRows();
            _skipNextParameterRefresh =
                _notificationDuringMutation && !_parameterRefreshDuringMutation;
        }

        if (removed)
        {
            _ = InvokeAsync(StateHasChanged);
        }

        return removed;
    }

    private bool MoveRow(
        SparseCollectionRow<TModel, TElement> row,
        int newIndex,
        long bindingVersion,
        out SparseCollectionEditError error
    )
    {
        if (_disposed || bindingVersion != Interlocked.Read(ref _bindingVersion))
        {
            error = SparseCollectionEditError.UnknownCollection;
            return false;
        }

        var editor = _editor;
        if (editor is null)
        {
            error = SparseCollectionEditError.UnknownCollection;
            return false;
        }

        var index = FindCurrentIndex(editor, row.UiKey);
        if (index < 0)
        {
            error = SparseCollectionEditError.IndexOutOfRange;
            return false;
        }

        BeginMutation();
        bool moved;
        try
        {
            moved = editor.TryMove(index, newIndex, out error);
        }
        finally
        {
            _mutationActive = false;
        }

        if (moved || _notificationDuringMutation || _parameterRefreshDuringMutation)
        {
            if (!_notificationDuringMutation)
            {
                InvalidateRows();
            }

            RefreshRows();
            _skipNextParameterRefresh =
                _notificationDuringMutation && !_parameterRefreshDuringMutation;
        }

        if (moved)
        {
            _ = InvokeAsync(StateHasChanged);
        }

        return moved;
    }

    private static int FindCurrentIndex(SparseCollectionEditor<TElement> editor, TElement item)
    {
        var count = editor.Count;
        for (var index = 0; index < count; index++)
        {
            if (editor.TryGetAt(index, out var current, out _) && ReferenceEquals(current, item))
            {
                return index;
            }
        }

        return -1;
    }

    private int FindCurrentIndex(SparseCollectionEditor<TElement> editor, Guid uiKey)
    {
        foreach (var currentRow in Rows)
        {
            if (currentRow.UiKey == uiKey)
            {
                return FindCurrentIndex(editor, currentRow.Item);
            }
        }

        return -1;
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        InvalidateRows();
        if (_mutationActive)
        {
            _notificationDuringMutation = true;
            return;
        }

        var bindingVersion = Interlocked.Read(ref _bindingVersion);
        var parameterSetVersion = Interlocked.Read(ref _parameterSetVersion);
        if (Interlocked.Exchange(ref _refreshQueued, 1) != 0)
        {
            return;
        }

        _ = InvokeAsync(async () =>
        {
            await Task.Yield();
            Interlocked.Exchange(ref _refreshQueued, 0);
            if (_disposed || bindingVersion != Interlocked.Read(ref _bindingVersion))
            {
                return;
            }

            var wasDirty = _builtRowsVersion != Interlocked.Read(ref _rowsVersion);
            RefreshRows();
            if (wasDirty && parameterSetVersion == Interlocked.Read(ref _parameterSetVersion))
            {
                _skipNextParameterRefresh = true;
            }

            StateHasChanged();
        });
    }

    private void BeginMutation()
    {
        _mutationActive = true;
        _notificationDuringMutation = false;
        _parameterRefreshDuringMutation = false;
    }

    private void InvalidateRows() => Interlocked.Increment(ref _rowsVersion);

    private void Unsubscribe()
    {
        if (_notifier is not null)
        {
            _notifier.PropertyChanged -= OnSessionChanged;
            _notifier = null;
        }
    }
}
