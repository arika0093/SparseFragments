using System.Collections;
using System.ComponentModel;
using SparseTypes.Generated;

namespace SparseTypes;

/// <summary>
/// Framework-neutral, opt-in validation error store for one root model type.
/// </summary>
/// <remarks>
/// <para>
/// The store is separate from generated models and edit sessions: creating it
/// is the opt-in, and sessions that never create one allocate nothing and
/// subscribe to nothing. Errors never touch the model or its change sets;
/// semantic dirty state and error state stay fully independent.
/// </para>
/// <para>
/// Errors are keyed by exact typed path plus origin (local validation, server
/// validation, rebase conflict, or any custom non-empty string), so replacing
/// or clearing one origin never erases another. Foreign-root and deleted or
/// missing-target paths are stored exactly, never dropped; they remain
/// reachable through typed queries and aggregate queries.
/// </para>
/// <para>
/// Path versus property-name boundary: typed APIs (<c>GetPathErrors</c>,
/// <c>HasPathErrors</c>) match exact paths and their descendants. The
/// <see cref="System.ComponentModel.INotifyDataErrorInfo"/> property-name API
/// parses the name as wire path text for <typeparamref name="TModel"/>
/// (<c>null</c> or empty aggregates every error; unparseable names yield no
/// errors). Non-string key spellings that parse back as string keys still
/// match through their wire text. Dotted and bracket spellings are this
/// library's wire grammar; universal binding-engine support for nested paths
/// is not claimed.
/// </para>
/// <para>
/// There is no validation engine here: no attributes run implicitly and
/// nothing validates on keystroke. Applications feed results explicitly, for
/// example by reading <c>Descriptor.Attributes</c> (DataAnnotations or other
/// validators stay application-owned, including execution and cancellation)
/// and calling <c>SetErrors</c>, or by projecting <see cref="SparseConflict"/>
/// records with <c>AddConflicts</c>, which never resolves conflicts.
/// </para>
/// <para>
/// Lifetime policies are application-owned: reload the baseline with a fresh
/// store (or <c>Clear</c>), drop stale conflicts with
/// <c>ClearOrigin(SparseValidationOrigins.RebaseConflict)</c> after accept or
/// reconcile, and refresh local or server origins per validation pass or
/// round-trip. Stores are independent across sessions.
/// </para>
/// <para>
/// Threading: mutations and queries are lock-guarded and may be called from
/// any thread. <see cref="ErrorsChanged"/> fires synchronously on the
/// mutating thread with no captured synchronization context; UI handlers must
/// marshal themselves. Disposal clears errors with a final notification, then
/// detaches handlers; later use throws <see cref="ObjectDisposedException"/>.
/// Trimming/AOT safe: no reflection, no dynamic dispatch, no new packages.
/// </para>
/// </remarks>
/// <typeparam name="TModel">The root model type errors are recorded against.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseValidationStore<TModel> : INotifyDataErrorInfo, IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<(SparsePath Path, string Origin), List<object?>> _buckets = new();
    private int _totalCount;
    private bool _disposed;

    /// <summary>Gets the root model type this store is bound to.</summary>
    public Type RootType => typeof(TModel);

    /// <summary>Gets whether the store currently holds any errors.</summary>
    public bool HasErrors
    {
        get
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                return _totalCount > 0;
            }
        }
    }

    /// <summary>Gets whether the store has been disposed.</summary>
    public bool IsDisposed
    {
        get
        {
            lock (_sync)
            {
                return _disposed;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    /// <summary>Replaces the errors recorded at a path for one origin.</summary>
    /// <param name="path">The typed path to record at; stored exactly, even when deleted or foreign.</param>
    /// <param name="origin">The origin to replace; other origins at the path stay intact.</param>
    /// <param name="errors">The replacement messages or objects; empty clears that origin at the path.</param>
    public void SetErrors(SparsePath path, string origin, IEnumerable<object?> errors)
    {
        ArgumentException.ThrowIfNullOrEmpty(origin);
        ArgumentNullException.ThrowIfNull(errors);
        var materialized = Materialize(errors);
        List<string> notifications;
        EventHandler<DataErrorsChangedEventArgs>? handler;
        lock (_sync)
        {
            ThrowIfDisposed();
            var before = GetExactErrorsLocked(path);
            ApplyReplacementLocked(path, origin, materialized);
            notifications = ChangedChainLocked(path, before);
            handler = ErrorsChanged;
        }

        Raise(notifications, handler);
    }

    /// <summary>Replaces the errors recorded at a typed path for one origin.</summary>
    /// <param name="path">The typed path to record at.</param>
    /// <param name="origin">The origin to replace; other origins at the path stay intact.</param>
    /// <param name="errors">The replacement messages or objects; empty clears that origin at the path.</param>
    /// <typeparam name="TValue">The addressed value type.</typeparam>
    public void SetErrors<TValue>(
        SparsePath<TModel, TValue> path,
        string origin,
        IEnumerable<object?> errors
    ) => SetErrors(path.Path, origin, errors);

    /// <summary>Replaces the errors recorded at a descriptor path for one origin.</summary>
    /// <param name="descriptor">The descriptor whose path is recorded at; its Attributes stay for the caller to interpret.</param>
    /// <param name="origin">The origin to replace; other origins at the path stay intact.</param>
    /// <param name="errors">The replacement messages or objects; empty clears that origin at the path.</param>
    public void SetErrors(IDescriptor descriptor, string origin, IEnumerable<object?> errors)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        SetErrors(descriptor.Path, origin, errors);
    }

    /// <summary>Records one message at a path for one origin, replacing that origin.</summary>
    /// <param name="path">The typed path to record at; stored exactly, even when deleted or foreign.</param>
    /// <param name="origin">The origin to replace; other origins at the path stay intact.</param>
    /// <param name="message">The message to record.</param>
    public void SetErrors(SparsePath path, string origin, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        SetErrors(path, origin, [message]);
    }

    /// <summary>Records one message at a typed path for one origin, replacing that origin.</summary>
    /// <param name="path">The typed path to record at.</param>
    /// <param name="origin">The origin to replace; other origins at the path stay intact.</param>
    /// <param name="message">The message to record.</param>
    /// <typeparam name="TValue">The addressed value type.</typeparam>
    public void SetErrors<TValue>(SparsePath<TModel, TValue> path, string origin, string message) =>
        SetErrors(path.Path, origin, message);

    /// <summary>Records one message at a descriptor path for one origin, replacing that origin.</summary>
    /// <param name="descriptor">The descriptor whose path is recorded at; its Attributes stay for the caller to interpret.</param>
    /// <param name="origin">The origin to replace; other origins at the path stay intact.</param>
    /// <param name="message">The message to record.</param>
    public void SetErrors(IDescriptor descriptor, string origin, string message)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        SetErrors(descriptor.Path, origin, message);
    }

    /// <summary>Clears errors at a path, optionally for one origin only.</summary>
    /// <param name="path">The path to clear.</param>
    /// <param name="origin">The origin to clear, or null for every origin at the path.</param>
    public void ClearErrors(SparsePath path, string? origin = null)
    {
        if (origin is not null)
        {
            ArgumentException.ThrowIfNullOrEmpty(origin);
        }

        List<string> notifications;
        EventHandler<DataErrorsChangedEventArgs>? handler;
        lock (_sync)
        {
            ThrowIfDisposed();
            var before = GetExactErrorsLocked(path);
            if (origin is null)
            {
                RemovePathLocked(path);
            }
            else
            {
                RemoveBucketLocked(path, origin);
            }

            notifications = ChangedChainLocked(path, before);
            handler = ErrorsChanged;
        }

        Raise(notifications, handler);
    }

    /// <summary>Clears errors at a typed path, optionally for one origin only.</summary>
    /// <param name="path">The typed path to clear.</param>
    /// <param name="origin">The origin to clear, or null for every origin at the path.</param>
    /// <typeparam name="TValue">The addressed value type.</typeparam>
    public void ClearErrors<TValue>(SparsePath<TModel, TValue> path, string? origin = null) =>
        ClearErrors(path.Path, origin);

    /// <summary>Clears errors at a descriptor path, optionally for one origin only.</summary>
    /// <param name="descriptor">The descriptor whose path is cleared.</param>
    /// <param name="origin">The origin to clear, or null for every origin at the path.</param>
    public void ClearErrors(IDescriptor descriptor, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ClearErrors(descriptor.Path, origin);
    }

    /// <summary>Clears every error recorded under one origin, across all paths.</summary>
    /// <param name="origin">The origin to clear; other origins stay intact.</param>
    public void ClearOrigin(string origin)
    {
        ArgumentException.ThrowIfNullOrEmpty(origin);
        List<string> notifications = new();
        EventHandler<DataErrorsChangedEventArgs>? handler;
        lock (_sync)
        {
            ThrowIfDisposed();
            foreach (var path in DistinctPathsLocked())
            {
                var before = GetExactErrorsLocked(path);
                RemoveBucketLocked(path, origin);
                AppendChangedChainLocked(notifications, path, before);
            }

            handler = ErrorsChanged;
        }

        Raise(notifications, handler);
    }

    /// <summary>Clears every error in the store, across all paths and origins.</summary>
    public void Clear()
    {
        List<string> notifications = new();
        EventHandler<DataErrorsChangedEventArgs>? handler;
        lock (_sync)
        {
            ThrowIfDisposed();
            foreach (var path in DistinctPathsLocked())
            {
                var before = GetExactErrorsLocked(path);
                RemovePathLocked(path);
                AppendChangedChainLocked(notifications, path, before);
            }

            handler = ErrorsChanged;
        }

        Raise(notifications, handler);
    }

    /// <summary>Projects structured rebase conflicts into errors without resolving them.</summary>
    /// <param name="conflicts">The conflicts to surface; every entry is stored, none dropped.</param>
    /// <param name="options">The origin, replace-first, and message policy; null keeps defaults.</param>
    /// <returns>The number of messages added.</returns>
    public int AddConflicts(
        IEnumerable<SparseConflict> conflicts,
        SparseValidationConflictOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        options ??= new SparseValidationConflictOptions();
        ArgumentException.ThrowIfNullOrEmpty(options.Origin);

        // Messages render before mutating so a throwing factory leaves the store untouched.
        var materialized = new List<(SparsePath Path, string Message)>();
        foreach (var conflict in conflicts)
        {
            ArgumentNullException.ThrowIfNull(conflict);
            materialized.Add((conflict.Path, options.GetMessage(conflict)));
        }

        List<string> notifications = new();
        EventHandler<DataErrorsChangedEventArgs>? handler;
        lock (_sync)
        {
            ThrowIfDisposed();
            if (options.ReplaceOriginFirst)
            {
                foreach (var path in DistinctPathsLocked())
                {
                    var before = GetExactErrorsLocked(path);
                    RemoveBucketLocked(path, options.Origin);
                    AppendChangedChainLocked(notifications, path, before);
                }
            }

            foreach (var (path, message) in materialized)
            {
                var before = GetExactErrorsLocked(path);
                AppendLocked(path, options.Origin, message);
                AppendChangedChainLocked(notifications, path, before);
            }

            handler = ErrorsChanged;
        }

        Raise(notifications, handler);
        return materialized.Count;
    }

    /// <summary>Determines whether a path or anything below it holds errors.</summary>
    /// <param name="path">The path to inspect, including its descendants.</param>
    public bool HasPathErrors(SparsePath path)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            return _buckets.Keys.Any(key => key.Path.Equals(path) || key.Path.StartsWith(path));
        }
    }

    /// <summary>Determines whether a typed path or anything below it holds errors.</summary>
    /// <param name="path">The typed path to inspect, including its descendants.</param>
    /// <typeparam name="TValue">The addressed value type.</typeparam>
    public bool HasPathErrors<TValue>(SparsePath<TModel, TValue> path) => HasPathErrors(path.Path);

    /// <summary>Gets the errors recorded at a path and anything below it.</summary>
    /// <param name="path">The path to inspect, including its descendants.</param>
    /// <returns>The merged errors in path-text then origin order; errors keep call order.</returns>
    public IReadOnlyList<object?> GetPathErrors(SparsePath path)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            var result = new List<object?>();
            foreach (
                var key in OrderedKeysLocked()
                    .Where(key => key.Path.Equals(path) || key.Path.StartsWith(path))
            )
            {
                result.AddRange(_buckets[key]);
            }

            return result;
        }
    }

    /// <summary>Gets the errors recorded at a typed path and anything below it.</summary>
    /// <param name="path">The typed path to inspect, including its descendants.</param>
    /// <returns>The merged errors in path-text then origin order; errors keep call order.</returns>
    /// <typeparam name="TValue">The addressed value type.</typeparam>
    public IReadOnlyList<object?> GetPathErrors<TValue>(SparsePath<TModel, TValue> path) =>
        GetPathErrors(path.Path);

    /// <summary>Gets every stored error with its path and origin.</summary>
    /// <returns>The snapshot entries in path-text then origin order; errors keep call order.</returns>
    public IReadOnlyList<SparseValidationError> GetAllErrors()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            var result = new List<SparseValidationError>(_totalCount);
            foreach (var key in OrderedKeysLocked())
            {
                foreach (var error in _buckets[key])
                {
                    result.Add(
                        new SparseValidationError
                        {
                            Path = key.Path,
                            Origin = key.Origin,
                            Error = error,
                        }
                    );
                }
            }

            return result;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A null or empty name aggregates every error. Any other name parses as
    /// wire path text for <typeparamref name="TModel"/> and returns that path
    /// plus its descendants; unparseable names yield no errors. The root path
    /// text (<c>$root</c>) therefore aggregates, matching the null query.
    /// </remarks>
    public IEnumerable GetErrors(string? propertyName)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (propertyName is not { Length: > 0 })
            {
                var all = new List<object?>(_totalCount);
                foreach (var key in OrderedKeysLocked())
                {
                    all.AddRange(_buckets[key]);
                }

                return all;
            }

            if (!SparsePath.TryParse(typeof(TModel), propertyName, out var query))
            {
                return Array.Empty<object?>();
            }

            var result = new List<object?>();
            foreach (
                var key in OrderedKeysLocked()
                    .Where(key =>
                        key.Path.Equals(query)
                        || key.Path.StartsWith(query)
                        || (
                            key.Path.RootType == typeof(TModel)
                            && PathTextMatches(key.Path, propertyName)
                        )
                    )
            )
            {
                result.AddRange(_buckets[key]);
            }

            return result;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Clears remaining errors with a final notification per affected property,
    /// detaches handlers, and marks the store disposed. Idempotent.
    /// </remarks>
    public void Dispose()
    {
        List<string> notifications = new();
        EventHandler<DataErrorsChangedEventArgs>? handler;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var path in DistinctPathsLocked())
            {
                var before = GetExactErrorsLocked(path);
                RemovePathLocked(path);
                AppendChangedChainLocked(notifications, path, before);
            }

            handler = ErrorsChanged;
            ErrorsChanged = null;
        }

        Raise(notifications, handler);
    }

    private static List<object?> Materialize(IEnumerable<object?> errors)
    {
        var materialized = new List<object?>();
        foreach (var error in errors)
        {
            if (error is null)
            {
                throw new ArgumentException(
                    "Validation errors must not contain null entries.",
                    nameof(errors)
                );
            }

            materialized.Add(error);
        }

        return materialized;
    }

    private void ApplyReplacementLocked(SparsePath path, string origin, List<object?> replacement)
    {
        var key = (path, origin);
        if (_buckets.TryGetValue(key, out var existing))
        {
            _totalCount -= existing.Count;
            _buckets.Remove(key);
        }

        if (replacement.Count > 0)
        {
            _buckets[key] = new List<object?>(replacement);
            _totalCount += replacement.Count;
        }
    }

    private void AppendLocked(SparsePath path, string origin, object error)
    {
        var key = (path, origin);
        if (!_buckets.TryGetValue(key, out var bucket))
        {
            bucket = new List<object?>();
            _buckets[key] = bucket;
        }

        bucket.Add(error);
        _totalCount++;
    }

    private void RemoveBucketLocked(SparsePath path, string origin)
    {
        var key = (path, origin);
        if (_buckets.TryGetValue(key, out var bucket))
        {
            _totalCount -= bucket.Count;
            _buckets.Remove(key);
        }
    }

    private void RemovePathLocked(SparsePath path)
    {
        var doomed = _buckets.Keys.Where(key => key.Path.Equals(path)).ToList();
        foreach (var key in doomed)
        {
            _totalCount -= _buckets[key].Count;
            _buckets.Remove(key);
        }
    }

    private List<(SparsePath Path, string Origin)> OrderedKeysLocked()
    {
        var keys = new List<(SparsePath Path, string Origin)>(_buckets.Keys);
        keys.Sort(
            static (left, right) =>
            {
                var pathOrder = string.CompareOrdinal(left.Path.ToString(), right.Path.ToString());
                return pathOrder != 0
                    ? pathOrder
                    : string.CompareOrdinal(left.Origin, right.Origin);
            }
        );
        return keys;
    }

    private List<SparsePath> DistinctPathsLocked() =>
        OrderedKeysLocked().Select(static key => key.Path).Distinct().ToList();

    private List<object?> GetExactErrorsLocked(SparsePath path)
    {
        var result = new List<object?>();
        foreach (var key in OrderedKeysLocked().Where(key => key.Path.Equals(path)))
        {
            result.AddRange(_buckets[key]);
        }

        return result;
    }

    private List<string> ChangedChainLocked(SparsePath path, List<object?> before)
    {
        var notifications = new List<string>();
        AppendChangedChainLocked(notifications, path, before);
        return notifications;
    }

    private void AppendChangedChainLocked(List<string> sink, SparsePath path, List<object?> before)
    {
        if (ContentsEqual(before, GetExactErrorsLocked(path)))
        {
            return;
        }

        AppendChain(sink, path);
    }

    private static void AppendChain(List<string> sink, SparsePath path)
    {
        var current = path;
        while (true)
        {
            var name = current.IsRoot ? string.Empty : current.ToString();
            if (!sink.Contains(name))
            {
                sink.Add(name);
            }

            var parent = current.Parent;
            if (parent is null)
            {
                break;
            }

            current = parent.Value;
        }
    }

    private static bool ContentsEqual(List<object?> left, List<object?> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!Equals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool PathTextMatches(SparsePath path, string propertyName)
    {
        // Key-type-erased spellings (for example an int key parsed back as a
        // string key) still match through their wire text; separators keep
        // sibling member names from colliding.
        var text = path.ToString();
        return text.Equals(propertyName, StringComparison.Ordinal)
            || text.StartsWith(propertyName + ".", StringComparison.Ordinal)
            || text.StartsWith(propertyName + "[", StringComparison.Ordinal);
    }

    private void Raise(
        List<string> notifications,
        EventHandler<DataErrorsChangedEventArgs>? handler
    )
    {
        if (handler is null)
        {
            return;
        }

        foreach (var name in notifications)
        {
            handler(this, new DataErrorsChangedEventArgs(name));
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().FullName);
        }
    }
}
