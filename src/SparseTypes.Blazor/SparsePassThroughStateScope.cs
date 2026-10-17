using System.ComponentModel;

namespace SparseTypes.Blazor;

internal sealed class SparsePassThroughStateScope : IDisposable
{
    private interface IStateSource : IDisposable
    {
        bool TryFieldState<TValue>(
            SparsePath path,
            out SparseFieldState<TValue> state,
            out string? error
        );

        bool TryCollectionState(SparsePath path, out SparseCollectionState? state);
    }

    private sealed class StateSource<TModel> : IStateSource
        where TModel : class
    {
        private readonly object _sync = new();
        private readonly ISparseEditSession<TModel> _session;
        private readonly ISparseEditSessionWithStateSnapshot? _snapshotProvider;
        private readonly INotifyPropertyChanged? _notifier;
        private SparseChangeSetQueryView? _snapshot;
        private long _version;
        private long _snapshotVersion = -1;
        private bool _disposed;

        public StateSource(ISparseEditSession<TModel> session)
        {
            _session = session;
            _snapshotProvider = session as ISparseEditSessionWithStateSnapshot;
            if (session is INotifyPropertyChanged notifier)
            {
                _notifier = notifier;
                notifier.PropertyChanged += OnSessionChanged;
            }
        }

        public bool TryFieldState<TValue>(
            SparsePath path,
            out SparseFieldState<TValue> state,
            out string? error
        )
        {
            SparsePath<TModel, TValue> typedPath;
            try
            {
                typedPath = new SparsePath<TModel, TValue>(path);
            }
            catch (ArgumentException exception)
            {
                state = default;
                error = exception.Message;
                return false;
            }

            if (_snapshotProvider is null)
            {
                return SparseSessionInterop.TryFieldState(
                    _session,
                    typedPath,
                    out state,
                    out error
                );
            }

            if (!TryGetSnapshot(out var snapshot, out error) || snapshot is null)
            {
                state = default;
                return false;
            }

            try
            {
                state = snapshot.FieldState(typedPath);
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                state = default;
                error = exception.Message;
                return false;
            }
        }

        public bool TryCollectionState(SparsePath path, out SparseCollectionState? state)
        {
            if (SparseSessionInterop.TryCollectionState(_session, path, out state))
            {
                return true;
            }

            if (
                _snapshotProvider is not null
                && _session is ISparseEditSessionWithDescriptors descriptorSession
                && TryGetSnapshot(out var snapshot, out _)
                && snapshot is not null
            )
            {
                return SparseNestedCollectionStateQuery.TryGetState<TModel>(
                    snapshot.Changes,
                    descriptorSession.Descriptors,
                    path,
                    out state
                );
            }

            state = null;
            return false;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _snapshot = null;
            }

            if (_notifier is not null)
            {
                _notifier.PropertyChanged -= OnSessionChanged;
            }
        }

        private bool TryGetSnapshot(out SparseChangeSetQueryView? snapshot, out string? error)
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    snapshot = null;
                    error = "The pass-through state scope has been disposed.";
                    return false;
                }

                var version = Interlocked.Read(ref _version);
                if (_notifier is not null && _snapshot is not null && _snapshotVersion == version)
                {
                    snapshot = _snapshot;
                    error = null;
                    return true;
                }

                for (var attempt = 0; attempt < 3; attempt++)
                {
                    version = Interlocked.Read(ref _version);
                    if (
                        !SparseSessionInterop.TryCaptureStateSnapshot(
                            _session,
                            out var captured,
                            out error
                        ) || captured is null
                    )
                    {
                        snapshot = null;
                        return false;
                    }

                    if (version == Interlocked.Read(ref _version))
                    {
                        if (_notifier is not null)
                        {
                            _snapshot = captured;
                            _snapshotVersion = version;
                        }

                        snapshot = captured;
                        error = null;
                        return true;
                    }

                    if (attempt == 2)
                    {
                        snapshot = captured;
                        error = null;
                        return true;
                    }
                }

                snapshot = null;
                error = "The pass-through state snapshot could not be captured.";
                return false;
            }
        }

        private void OnSessionChanged(object? sender, PropertyChangedEventArgs args) =>
            Interlocked.Increment(ref _version);
    }

    private readonly IStateSource _source;
    private readonly SparsePath _prefix;
    private readonly bool _ownsSource;

    private SparsePassThroughStateScope(IStateSource source, SparsePath prefix, bool ownsSource)
    {
        _source = source;
        _prefix = prefix;
        _ownsSource = ownsSource;
    }

    public static SparsePassThroughStateScope CreateRoot<TModel>(ISparseEditSession<TModel> session)
        where TModel : class =>
        new(new StateSource<TModel>(session), SparsePath.Root<TModel>(), true);

    public SparsePassThroughStateScope Push(SparsePath path) =>
        new(_source, _prefix.Append(path), false);

    public bool TryFieldState<TValue>(
        SparsePath path,
        out SparseFieldState<TValue> state,
        out string? error
    ) => _source.TryFieldState(_prefix.Append(path), out state, out error);

    public bool TryCollectionState(SparsePath path, out SparseCollectionState? state) =>
        _source.TryCollectionState(_prefix.Append(path), out state);

    // Reports changes owned by the subtree at the path. Ancestor records are
    // excluded: a container reorder or resize does not change its rows, and
    // treating it as row dirt marked every sibling as edited and reforcked
    // clean children.
    public bool HasSubtreeChanges(SparsePath path)
    {
        if (!TryFieldState<object?>(path, out var state, out var error))
        {
            throw new InvalidOperationException(error);
        }

        return state.IsChanged || state.HasDescendantChange;
    }

    public void Dispose()
    {
        if (_ownsSource)
        {
            _source.Dispose();
        }
    }
}
