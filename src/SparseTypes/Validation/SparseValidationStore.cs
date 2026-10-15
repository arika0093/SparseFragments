using System.Collections;
using System.ComponentModel;
using SparseTypes.Generated;

namespace SparseTypes;

/// <summary>
/// Framework-neutral, opt-in validation error store for one root model type.
/// </summary>
/// <remarks>Creating it is the opt-in, and sessions that never create one allocate nothing and subscribe to nothing.</remarks>
/// <typeparam name="TModel">The root model type errors are recorded against.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseValidationStore<TModel> : INotifyDataErrorInfo, IDisposable
{
    private readonly object _sync = new();

    // Buckets index by path first, then origin, so exact single-path updates
    // touch only the origins at that path instead of sorting the whole store.
    private readonly Dictionary<SparsePath, Dictionary<string, List<object?>>> _buckets = new();
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
        var notifications = new OrderedNotifications();
        EventHandler<DataErrorsChangedEventArgs>? handler;
        lock (_sync)
        {
            ThrowIfDisposed();
            // One ordered pass: each visited path compares before/after, so
            // removing an origin that leaves an equivalent merged sequence
            // notifies nothing.
            foreach (var path in SortedPathsLocked())
            {
                var before = GetExactErrorsLocked(path);
                RemoveBucketLocked(path, origin);
                notifications.AppendChangedChain(path, before, GetExactErrorsLocked(path));
            }

            handler = ErrorsChanged;
        }

        Raise(notifications.Names, handler);
    }

    /// <summary>Clears every error in the store, across all paths and origins.</summary>
    public void Clear()
    {
        List<string> notifications;
        EventHandler<DataErrorsChangedEventArgs>? handler;
        lock (_sync)
        {
            ThrowIfDisposed();
            // Capture affected paths once, reset storage once, then notify.
            var affected = SortedPathsWithErrorsLocked();
            _buckets.Clear();
            _totalCount = 0;
            notifications = OrderedNotifications.ForPaths(affected);
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

        // Messages render before mutating so a throwing factory leaves the
        // store untouched.
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
                // One ordered pass without per-path whole-store sorts.
                var sink = new OrderedNotifications();
                foreach (var path in SortedPathsLocked())
                {
                    var before = GetExactErrorsLocked(path);
                    RemoveBucketLocked(path, options.Origin);
                    sink.AppendChangedChain(path, before, GetExactErrorsLocked(path));
                }

                notifications.AddRange(sink.Names);
            }

            // Group appends by path so each affected path compares once; the
            // sink keeps first-seen path order with ancestor deduplication.
            var sinkAfterReplace = OrderedNotifications.From(notifications);
            var hasOpenPath = false;
            var openPath = default(SparsePath);
            List<object?>? beforeForPath = null;
            foreach (var (path, message) in materialized)
            {
                if (beforeForPath is null || !path.Equals(openPath))
                {
                    if (beforeForPath is not null)
                    {
                        sinkAfterReplace.AppendChangedChain(
                            openPath,
                            beforeForPath,
                            GetExactErrorsLocked(openPath)
                        );
                    }

                    openPath = path;
                    hasOpenPath = true;
                    beforeForPath = GetExactErrorsLocked(path);
                }

                AppendLocked(path, options.Origin, message);
            }

            if (hasOpenPath)
            {
                sinkAfterReplace.AppendChangedChain(
                    openPath,
                    beforeForPath!,
                    GetExactErrorsLocked(openPath)
                );
            }

            notifications = sinkAfterReplace.Names;
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
            return _buckets.Keys.Any(stored => stored.Equals(path) || stored.StartsWith(path));
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
                var stored in SortedPathsLocked()
                    .Where(stored => stored.Equals(path) || stored.StartsWith(path))
            )
            {
                result.AddRange(GetExactErrorsLocked(stored));
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
            foreach (var path in SortedPathsLocked())
            {
                foreach (var origin in SortedOriginsLocked(path))
                {
                    foreach (var error in _buckets[path][origin])
                    {
                        result.Add(
                            new SparseValidationError
                            {
                                Path = path,
                                Origin = origin,
                                Error = error,
                            }
                        );
                    }
                }
            }

            return result;
        }
    }

    /// <inheritdoc />
    /// <remarks>A null or empty name aggregates every error.</remarks>
    public IEnumerable GetErrors(string? propertyName)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (propertyName is not { Length: > 0 })
            {
                var all = new List<object?>(_totalCount);
                foreach (var path in SortedPathsLocked())
                {
                    all.AddRange(GetExactErrorsLocked(path));
                }

                return all;
            }

            if (!SparsePath.TryParse(typeof(TModel), propertyName, out var query))
            {
                return Array.Empty<object?>();
            }

            var result = new List<object?>();
            foreach (var path in SortedPathsLocked())
            {
                if (
                    path.Equals(query)
                    || path.StartsWith(query)
                    || (path.RootType == typeof(TModel) && PathTextMatches(path, propertyName))
                )
                {
                    result.AddRange(GetExactErrorsLocked(path));
                }
            }

            return result;
        }
    }

    /// <inheritdoc />
    /// <remarks>Clears remaining errors with a final notification per affected property, detaches handlers, and marks the store disposed.</remarks>
    public void Dispose()
    {
        List<string> notifications;
        EventHandler<DataErrorsChangedEventArgs>? handler;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            // Single capture and reset, like Clear, then the same ordered
            // deduplicated ancestor notifications.
            var affected = SortedPathsWithErrorsLocked();
            _buckets.Clear();
            _totalCount = 0;
            notifications = OrderedNotifications.ForPaths(affected);
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
        // The materialized list is privately owned (never the caller's), so it
        // transfers into storage without a second copy.
        if (!_buckets.TryGetValue(path, out var origins))
        {
            if (replacement.Count == 0)
            {
                return;
            }

            origins = new Dictionary<string, List<object?>>(StringComparer.Ordinal);
            _buckets.Add(path, origins);
        }

        if (origins.TryGetValue(origin, out var existing))
        {
            _totalCount -= existing.Count;
            origins.Remove(origin);
        }

        if (replacement.Count > 0)
        {
            origins.Add(origin, replacement);
            _totalCount += replacement.Count;
        }

        if (origins.Count == 0)
        {
            _buckets.Remove(path);
        }
    }

    private void AppendLocked(SparsePath path, string origin, object error)
    {
        if (!_buckets.TryGetValue(path, out var origins))
        {
            origins = new Dictionary<string, List<object?>>(StringComparer.Ordinal);
            _buckets.Add(path, origins);
        }

        if (!origins.TryGetValue(origin, out var bucket))
        {
            bucket = new List<object?>();
            origins.Add(origin, bucket);
        }

        bucket.Add(error);
        _totalCount++;
    }

    private void RemoveBucketLocked(SparsePath path, string origin)
    {
        if (
            _buckets.TryGetValue(path, out var origins)
            && origins.TryGetValue(origin, out var bucket)
        )
        {
            origins.Remove(origin);
            _totalCount -= bucket.Count;
            if (origins.Count == 0)
            {
                _buckets.Remove(path);
            }
        }
    }

    private void RemovePathLocked(SparsePath path)
    {
        if (_buckets.Remove(path, out var origins))
        {
            foreach (var bucket in origins.Values)
            {
                _totalCount -= bucket.Count;
            }
        }
    }

    private List<SparsePath> SortedPathsLocked()
    {
        // One format per path; ordering stays path-text ordinal, as before.
        var dressed = new List<(string Text, SparsePath Path)>(_buckets.Count);
        foreach (var path in _buckets.Keys)
        {
            dressed.Add((path.ToString(), path));
        }

        dressed.Sort(static (left, right) => string.CompareOrdinal(left.Text, right.Text));
        var sorted = new List<SparsePath>(dressed.Count);
        foreach (var entry in dressed)
        {
            sorted.Add(entry.Path);
        }

        return sorted;
    }

    private List<SparsePath> SortedPathsWithErrorsLocked()
    {
        // Buckets never retain empty origins or paths, so every stored path
        // holds errors; no before/after materialization is needed here.
        return SortedPathsLocked();
    }

    private List<string> SortedOriginsLocked(SparsePath path)
    {
        var origins = new List<string>(_buckets[path].Keys);
        origins.Sort(StringComparer.Ordinal);
        return origins;
    }

    private List<object?> GetExactErrorsLocked(SparsePath path)
    {
        // Only the origins at this path, in origin order: no whole-store sort.
        var result = new List<object?>();
        if (_buckets.TryGetValue(path, out var origins))
        {
            var names = new List<string>(origins.Keys);
            names.Sort(StringComparer.Ordinal);
            foreach (var origin in names)
            {
                result.AddRange(origins[origin]);
            }
        }

        return result;
    }

    private List<string> ChangedChainLocked(SparsePath path, List<object?> before)
    {
        var sink = new OrderedNotifications();
        sink.AppendChangedChain(path, before, GetExactErrorsLocked(path));
        return sink.Names;
    }

    // Ordered, deduplicated ancestor notifications: wire-text identity stays
    // separate from typed path identity, compared ordinally.
    private sealed class OrderedNotifications
    {
        private readonly List<string> _names;
        private readonly HashSet<string> _seen;

        public OrderedNotifications()
        {
            _names = new List<string>();
            _seen = new HashSet<string>(StringComparer.Ordinal);
        }

        private OrderedNotifications(List<string> names)
        {
            _names = names;
            _seen = new HashSet<string>(names, StringComparer.Ordinal);
        }

        public List<string> Names => _names;

        public static OrderedNotifications From(List<string> names) => new(names);

        public static List<string> ForPaths(List<SparsePath> paths)
        {
            var sink = new OrderedNotifications();
            foreach (var path in paths)
            {
                sink.AppendChain(path);
            }

            return sink.Names;
        }

        public void AppendChangedChain(SparsePath path, List<object?> before, List<object?> after)
        {
            if (ContentsEqual(before, after))
            {
                return;
            }

            AppendChain(path);
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

        public void AppendChain(SparsePath path)
        {
            var current = path;
            while (true)
            {
                var name = current.IsRoot ? string.Empty : current.ToString();
                if (_seen.Add(name))
                {
                    _names.Add(name);
                }

                var parent = current.Parent;
                if (parent is null)
                {
                    break;
                }

                current = parent.Value;
            }
        }
    }

    private static bool PathTextMatches(SparsePath path, string propertyName)
    {
        // Key-type-erased spellings (for example an int key parsed back as a
        // string key) still match through their wire text.
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
