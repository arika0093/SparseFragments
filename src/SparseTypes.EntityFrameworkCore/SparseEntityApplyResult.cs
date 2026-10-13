using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using SparseTypes.EntityFrameworkCore.Mapping;

namespace SparseTypes.EntityFrameworkCore;

/// <summary>The kind of operation recorded during an entity apply.</summary>
public enum SparseEntityApplyOperationKind
{
    /// <summary>A mapped entity member was assigned a value.</summary>
    Set,

    /// <summary>A mapped entity member was assigned null.</summary>
    SetNull,

    /// <summary>A missing DTO value was intentionally ignored.</summary>
    MissingIgnored,

    /// <summary>A child entity was created.</summary>
    Created,

    /// <summary>A child entity was removed.</summary>
    Removed,

    /// <summary>A keyed collection was reordered.</summary>
    Reordered,

    /// <summary>A caller-supplied writer handled the changed value.</summary>
    CustomWrite,
}

/// <summary>Describes one operation performed while applying a change set.</summary>
public sealed record SparseEntityAppliedPath
{
    /// <summary>Creates a description of one applied write.</summary>
    /// <param name="dtoPath">The dotted DTO path.</param>
    /// <param name="operation">The performed operation.</param>
    /// <param name="entityType">The entity CLR type, when known.</param>
    /// <param name="entityMember">The entity member, when known.</param>
    /// <param name="entityKey">The entity identity, when known.</param>
    /// <param name="removeKind">The configured removal policy for a removal.</param>
    public SparseEntityAppliedPath(
        string dtoPath,
        SparseEntityApplyOperationKind operation,
        string? entityType = null,
        string? entityMember = null,
        object? entityKey = null,
        SparseEntityRemoveKind? removeKind = null
    )
    {
        ArgumentNullException.ThrowIfNull(dtoPath);
        DtoPath = dtoPath;
        Operation = operation;
        EntityType = entityType;
        EntityMember = entityMember;
        EntityKey = entityKey;
        RemoveKind = removeKind;
    }

    /// <summary>Gets the dotted DTO path.</summary>
    public string DtoPath { get; init; }

    /// <summary>Gets the performed operation.</summary>
    public SparseEntityApplyOperationKind Operation { get; init; }

    /// <summary>Gets the entity CLR type, when known.</summary>
    public string? EntityType { get; init; }

    /// <summary>Gets the entity member, when known.</summary>
    public string? EntityMember { get; init; }

    /// <summary>Gets the entity identity, when known.</summary>
    public object? EntityKey { get; init; }

    /// <summary>Gets the configured removal policy for a removal.</summary>
    public SparseEntityRemoveKind? RemoveKind { get; init; }
}

/// <summary>
/// The outcome of one <c>ApplyWithResult</c> call.
/// </summary>
/// <remarks>
/// Successful writes are recorded in <see cref="AppliedPaths" />. Keyed
/// collection synchronization also records each created child entity under
/// its DTO <c>[SparseTemporaryKey]</c> value so the application can correlate
/// database-generated permanent keys across <c>SaveChanges</c>. Correlations
/// are scoped by DTO collection path: the same temporary value reused under a
/// different collection resolves independently there. Nested scopes additionally
/// resolve by parent scope (stable key or temporary identity), so parents
/// whose display keys collide still correlate independently; direct nested
/// path-string lookups keep their merged legacy view.
/// </remarks>
public sealed class SparseEntityApplyResult
{
    private Dictionary<Guid, IReadOnlyList<object>>? _byTemporaryKey;
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, object>> _createdByPath;
    private readonly IReadOnlyDictionary<string, NestedScopeTable> _nestedScopes;
    private readonly IReadOnlyList<SparseEntityAppliedPath> _appliedPaths;
    private readonly IReadOnlyDictionary<
        string,
        IReadOnlyDictionary<object, string>
    > _collectionScopeKeys;

    internal SparseEntityApplyResult(
        Dictionary<string, Dictionary<Guid, object>> byPath,
        IEnumerable<SparseEntityAppliedPath>? appliedPaths = null,
        Dictionary<string, Dictionary<object, string>>? collectionScopeKeys = null,
        IReadOnlyDictionary<string, NestedScopeTable>? nestedScopes = null
    )
    {
        ArgumentNullException.ThrowIfNull(byPath);
        _nestedScopes =
            nestedScopes
            ?? new ReadOnlyDictionary<string, NestedScopeTable>(
                new Dictionary<string, NestedScopeTable>()
            );
        var pathViews = new Dictionary<string, IReadOnlyDictionary<Guid, object>>(
            byPath.Count,
            StringComparer.Ordinal
        );
        foreach (var path in byPath)
        {
            pathViews.Add(path.Key, new ReadOnlyDictionary<Guid, object>(path.Value));
        }

        _createdByPath = new ReadOnlyDictionary<string, IReadOnlyDictionary<Guid, object>>(
            pathViews
        );
        _appliedPaths = Array.AsReadOnly(appliedPaths?.ToArray() ?? []);
        var scopeViews = new Dictionary<string, IReadOnlyDictionary<object, string>>(
            collectionScopeKeys?.Count ?? 0,
            StringComparer.Ordinal
        );
        if (collectionScopeKeys is not null)
        {
            foreach (var scope in collectionScopeKeys)
            {
                scopeViews.Add(scope.Key, new ReadOnlyDictionary<object, string>(scope.Value));
            }
        }

        _collectionScopeKeys = new ReadOnlyDictionary<string, IReadOnlyDictionary<object, string>>(
            scopeViews
        );
    }

    /// <summary>Gets created child entities by DTO collection path, then temporary key.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, object>> CreatedByPath =>
        _createdByPath;

    /// <summary>Gets all operations recorded by this apply in execution order.</summary>
    public IReadOnlyList<SparseEntityAppliedPath> AppliedPaths => _appliedPaths;

    internal IReadOnlyDictionary<object, string> GetCreatedScopeKeys(string collectionPath) =>
        _collectionScopeKeys.TryGetValue(collectionPath, out var scopes) ? scopes : EmptyScopeKeys;

    /// <summary>Collects recorded members known to belong to sibling scopes.</summary>
    /// <param name="collectionPath">The dotted DTO collection path.</param>
    /// <param name="mine">The parent scopes of the current item.</param>
    /// <returns>Members recorded under other scopes; empty when unknown.</returns>
    internal HashSet<object> ForeignMembers(
        string collectionPath,
        IReadOnlyList<CollectionScopeIdentity> mine
    )
    {
        ArgumentNullException.ThrowIfNull(collectionPath);
        ArgumentNullException.ThrowIfNull(mine);
        if (!_nestedScopes.TryGetValue(collectionPath, out var table))
        {
            return [];
        }

        return table.ForeignMembers(mine);
    }

    /// <summary>Looks up created children for one nested collection path and parent scope.</summary>
    /// <param name="collectionPath">The dotted DTO collection path.</param>
    /// <param name="scope">The parent element scope.</param>
    /// <returns>The temporary-to-entity map, or null when nothing was recorded.</returns>
    internal IReadOnlyDictionary<Guid, object>? GetCreated(
        string collectionPath,
        CollectionScopeIdentity scope
    )
    {
        ArgumentNullException.ThrowIfNull(collectionPath);
        if (
            scope.Kind == CollectionScopeKind.Unscoped
            || !_nestedScopes.TryGetValue(collectionPath, out var table)
        )
        {
            return null;
        }

        return table.TryGet(scope);
    }

    /// <summary>Gets whether any child entity was created by this apply.</summary>
    public bool HasCreatedEntities => _createdByPath.Count > 0;

    /// <summary>Looks up the created entities for a temporary key across every collection path.</summary>
    /// <param name="temporaryKey">The DTO temporary identity.</param>
    /// <returns>The created child entities carrying that temporary identity.</returns>
    public IReadOnlyList<object> GetCreated(Guid temporaryKey) =>
        GetByTemporaryKey().TryGetValue(temporaryKey, out var entities)
            ? entities
            : Array.Empty<object>();

    /// <summary>Gets created child entities for one DTO collection path.</summary>
    /// <param name="collectionPath">The dotted DTO collection path.</param>
    /// <returns>The created entities indexed by their temporary identity.</returns>
    public IReadOnlyDictionary<Guid, object> GetCreated(string collectionPath)
    {
        ArgumentNullException.ThrowIfNull(collectionPath);
        return _createdByPath.TryGetValue(collectionPath, out var entities)
            ? entities
            : EmptyCreatedByPath;
    }

    /// <summary>Gets created child entities for one DTO collection path with a typed view.</summary>
    /// <typeparam name="TEntityChild">The expected child entity type.</typeparam>
    /// <param name="collectionPath">The dotted DTO collection path.</param>
    /// <returns>A typed, read-only view indexed by temporary identity.</returns>
    public IReadOnlyDictionary<Guid, TEntityChild> GetCreated<TEntityChild>(string collectionPath)
        where TEntityChild : class
    {
        ArgumentNullException.ThrowIfNull(collectionPath);
        return new TypedCreatedView<TEntityChild>(GetCreated(collectionPath));
    }

    /// <summary>Gets created entities for a DTO collection selected by a member expression.</summary>
    /// <typeparam name="TDto">The DTO type that declares the collection.</typeparam>
    /// <typeparam name="TEntityChild">The expected child entity type.</typeparam>
    /// <param name="collection">The DTO collection member.</param>
    /// <returns>A typed, read-only view indexed by temporary identity.</returns>
    public IReadOnlyDictionary<Guid, TEntityChild> GetCreated<TEntityChild, TDto>(
        Expression<Func<TDto, object?>> collection
    )
        where TEntityChild : class
        where TDto : class
    {
        ArgumentNullException.ThrowIfNull(collection);
        return GetCreated<TEntityChild>(GetCollectionPath(collection));
    }

    /// <summary>Looks up a created child by collection path and temporary identity.</summary>
    /// <param name="collectionPath">The dotted DTO collection path.</param>
    /// <param name="temporaryKey">The DTO temporary identity.</param>
    /// <param name="entity">The created entity, when found and of the expected type.</param>
    /// <typeparam name="TEntityChild">The expected child entity type.</typeparam>
    /// <returns>True when that path contains an entity of the expected type.</returns>
    public bool TryGetCreated<TEntityChild>(
        string collectionPath,
        Guid temporaryKey,
        [NotNullWhen(true)] out TEntityChild? entity
    )
        where TEntityChild : class
    {
        entity = null;
        if (
            !GetCreated(collectionPath).TryGetValue(temporaryKey, out var untyped)
            || untyped is not TEntityChild typed
        )
        {
            return false;
        }

        entity = typed;
        return true;
    }

    /// <summary>Looks up the single created entity for a temporary key.</summary>
    /// <param name="temporaryKey">The DTO temporary identity.</param>
    /// <param name="entity">The created child entity.</param>
    /// <typeparam name="TEntityChild">The expected child entity type.</typeparam>
    /// <returns>True when exactly one created entity of that type carries the key.</returns>
    public bool TryGetCreated<TEntityChild>(Guid temporaryKey, out TEntityChild? entity)
        where TEntityChild : class
    {
        entity = null;
        if (!GetByTemporaryKey().TryGetValue(temporaryKey, out var entities) || entities.Count != 1)
        {
            return false;
        }

        entity = entities[0] as TEntityChild;
        return entity is not null;
    }

    private Dictionary<Guid, IReadOnlyList<object>> GetByTemporaryKey()
    {
        var cached = Volatile.Read(ref _byTemporaryKey);
        if (cached is not null)
        {
            return cached;
        }

        var entitiesByKey = new Dictionary<Guid, List<object>>();
        foreach (var path in _createdByPath.Values)
        {
            foreach (var entry in path)
            {
                if (!entitiesByKey.TryGetValue(entry.Key, out var entities))
                {
                    entities = [];
                    entitiesByKey.Add(entry.Key, entities);
                }

                entities.Add(entry.Value);
            }
        }

        var byTemporaryKey = new Dictionary<Guid, IReadOnlyList<object>>(entitiesByKey.Count);
        foreach (var entry in entitiesByKey)
        {
            byTemporaryKey.Add(entry.Key, entry.Value.AsReadOnly());
        }

        return Interlocked.CompareExchange(ref _byTemporaryKey, byTemporaryKey, null)
            ?? byTemporaryKey;
    }

    private static IReadOnlyDictionary<Guid, object> EmptyCreatedByPath { get; } =
        new ReadOnlyDictionary<Guid, object>(new Dictionary<Guid, object>());

    private static IReadOnlyDictionary<object, string> EmptyScopeKeys { get; } =
        new ReadOnlyDictionary<object, string>(
            new Dictionary<object, string>(ReferenceEqualityComparer.Instance)
        );

    private static string GetCollectionPath<TDto>(Expression<Func<TDto, object?>> collection)
    {
        Expression expression = collection.Body;
        while (
            expression is UnaryExpression unary
            && unary.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked
        )
        {
            expression = unary.Operand;
        }

        var members = new Stack<string>();
        while (expression is MemberExpression member)
        {
            members.Push(member.Member.Name);
            expression = member.Expression!;
        }

        if (expression != collection.Parameters[0] || members.Count == 0)
        {
            throw new ArgumentException(
                "The collection selector must be a member path rooted at its DTO parameter.",
                nameof(collection)
            );
        }

        return string.Join(".", members);
    }

    private sealed class TypedCreatedView<TEntityChild>(IReadOnlyDictionary<Guid, object> source)
        : IReadOnlyDictionary<Guid, TEntityChild>
        where TEntityChild : class
    {
        public int Count => source.Count;

        public IEnumerable<Guid> Keys => source.Keys;

        public IEnumerable<TEntityChild> Values => EnumerateValues();

        public TEntityChild this[Guid key] => Cast(source[key]);

        public bool ContainsKey(Guid key) => source.ContainsKey(key);

        public bool TryGetValue(Guid key, [MaybeNullWhen(false)] out TEntityChild value)
        {
            value = default;
            if (!source.TryGetValue(key, out var untyped) || untyped is not TEntityChild typed)
            {
                return false;
            }

            value = typed;
            return true;
        }

        public IEnumerator<KeyValuePair<Guid, TEntityChild>> GetEnumerator()
        {
            foreach (var entry in source)
            {
                yield return new KeyValuePair<Guid, TEntityChild>(entry.Key, Cast(entry.Value));
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();

        private IEnumerable<TEntityChild> EnumerateValues()
        {
            foreach (var value in source.Values)
            {
                yield return Cast(value);
            }
        }

        private static TEntityChild Cast(object value) =>
            value as TEntityChild
            ?? throw new InvalidCastException(
                $"The created entity is not of type '{typeof(TEntityChild).Name}'."
            );
    }
}
