using System.Linq.Expressions;
using SparseFragments.EntityFrameworkCore.Mapping;

namespace SparseFragments.EntityFrameworkCore;

/// <summary>
/// Declares the mapping between one entity and one DTO type.
/// </summary>
/// <remarks>
/// Same-name scalar members are inferred; this builder registers only
/// overrides (Member overloads), specialized writers (Write), nested scopes
/// (Object), collection markers (Collection), ignores (Ignore), and policies
/// (OnMissing, AllowWrite, MissingDefault). An explicitly configured DTO path
/// replaces its inferred default instead of running both.
/// </remarks>
/// <typeparam name="TEntity">The tracked EF Core entity type.</typeparam>
/// <typeparam name="TDto">The DTO (fragment model) type carrying the change set.</typeparam>
public sealed class SparseEntityMapBuilder<TEntity, TDto> : SparseEntityMapBuilderBase
    where TEntity : class
    where TDto : class
{
    /// <summary>
    /// Maps a DTO member to an entity member with the same value type.
    /// </summary>
    /// <param name="dto">Selects the DTO member (single level).</param>
    /// <param name="entity">Selects the entity member; intermediate segments must be reference navigations.</param>
    /// <typeparam name="TDtoValue">The DTO member type.</typeparam>
    /// <typeparam name="TEntityValue">The entity member type.</typeparam>
    /// <returns>This builder.</returns>
    public SparseEntityMapBuilder<TEntity, TDto> Member<TDtoValue, TEntityValue>(
        Expression<Func<TDto, TDtoValue?>> dto,
        Expression<Func<TEntity, TEntityValue?>> entity
    )
    {
        AddNode(new ScalarNode<TDto, TEntity, TDtoValue, TEntityValue>(dto, entity, null, null));
        return this;
    }

    /// <summary>
    /// Maps a DTO member to an entity member through a conversion callback.
    /// </summary>
    /// <param name="dto">Selects the DTO member (single level).</param>
    /// <param name="entity">Selects the entity member; intermediate segments must be reference navigations.</param>
    /// <param name="convert">Converts the DTO value to the entity value. Expected to be pure: it runs before any write.</param>
    /// <typeparam name="TDtoValue">The DTO member type.</typeparam>
    /// <typeparam name="TEntityValue">The entity member type.</typeparam>
    /// <returns>This builder.</returns>
    public SparseEntityMapBuilder<TEntity, TDto> Member<TDtoValue, TEntityValue>(
        Expression<Func<TDto, TDtoValue?>> dto,
        Expression<Func<TEntity, TEntityValue?>> entity,
        Func<TDtoValue?, TEntityValue?> convert
    )
    {
        ArgumentNullException.ThrowIfNull(convert);
        AddNode(new ScalarNode<TDto, TEntity, TDtoValue, TEntityValue>(dto, entity, convert, null));
        return this;
    }

    /// <summary>
    /// Maps a DTO member to an entity member with a missing-value policy.
    /// </summary>
    /// <param name="dto">Selects the DTO member (single level).</param>
    /// <param name="entity">Selects the entity member; intermediate segments must be reference navigations.</param>
    /// <param name="onMissing">Handles a changed-to-missing after-state for this member.</param>
    /// <typeparam name="TDtoValue">The DTO member type.</typeparam>
    /// <typeparam name="TEntityValue">The entity member type.</typeparam>
    /// <returns>This builder.</returns>
    public SparseEntityMapBuilder<TEntity, TDto> Member<TDtoValue, TEntityValue>(
        Expression<Func<TDto, TDtoValue?>> dto,
        Expression<Func<TEntity, TEntityValue?>> entity,
        SparseEntityMissingPolicy onMissing
    )
    {
        AddNode(
            new ScalarNode<TDto, TEntity, TDtoValue, TEntityValue>(dto, entity, null, onMissing)
        );
        return this;
    }

    /// <summary>
    /// Maps a DTO member to an entity member through a conversion callback with a missing-value policy.
    /// </summary>
    /// <param name="dto">Selects the DTO member (single level).</param>
    /// <param name="entity">Selects the entity member; intermediate segments must be reference navigations.</param>
    /// <param name="convert">Converts the DTO value to the entity value. Expected to be pure: it runs before any write.</param>
    /// <param name="onMissing">Handles a changed-to-missing after-state for this member.</param>
    /// <typeparam name="TDtoValue">The DTO member type.</typeparam>
    /// <typeparam name="TEntityValue">The entity member type.</typeparam>
    /// <returns>This builder.</returns>
    public SparseEntityMapBuilder<TEntity, TDto> Member<TDtoValue, TEntityValue>(
        Expression<Func<TDto, TDtoValue?>> dto,
        Expression<Func<TEntity, TEntityValue?>> entity,
        Func<TDtoValue?, TEntityValue?> convert,
        SparseEntityMissingPolicy onMissing
    )
    {
        ArgumentNullException.ThrowIfNull(convert);
        AddNode(
            new ScalarNode<TDto, TEntity, TDtoValue, TEntityValue>(dto, entity, convert, onMissing)
        );
        return this;
    }

    /// <summary>
    /// Handles a DTO member with a specialized writer instead of a property write.
    /// </summary>
    /// <remarks>
    /// The writer runs only for changed members and receives the presence-aware
    /// after-state, so it distinguishes unchanged, missing, present null, and
    /// present values itself. Missing policies do not apply to writers.
    /// </remarks>
    /// <param name="dto">Selects the DTO member (single level).</param>
    /// <param name="writer">Writes the after-state to the entity.</param>
    /// <typeparam name="TDtoValue">The DTO member type.</typeparam>
    /// <returns>This builder.</returns>
    public SparseEntityMapBuilder<TEntity, TDto> Write<TDtoValue>(
        Expression<Func<TDto, TDtoValue?>> dto,
        Action<TEntity, Optional<TDtoValue?>> writer
    )
    {
        ArgumentNullException.ThrowIfNull(writer);
        AddNode(new WriteNode<TDto, TEntity, TDtoValue>(dto, writer));
        return this;
    }

    /// <summary>
    /// Suppresses the mapping for a DTO member. Changes under it are skipped.
    /// </summary>
    /// <param name="dto">Selects the DTO member (single level).</param>
    /// <typeparam name="TValue">The DTO member type.</typeparam>
    /// <returns>This builder.</returns>
    public SparseEntityMapBuilder<TEntity, TDto> Ignore<TValue>(Expression<Func<TDto, TValue?>> dto)
    {
        AddIgnored(MemberPath.Parse(dto).SingleName("DTO"));
        return this;
    }

    /// <summary>
    /// Maps a nested DTO object onto a reference navigation with a scoped child mapping.
    /// </summary>
    /// <remarks>
    /// The child mapping is scoped to this parent path: the same child type pair
    /// may map differently under another parent.
    /// </remarks>
    /// <param name="dto">Selects the nested DTO member (single level).</param>
    /// <param name="entity">Selects the reference navigation (single level).</param>
    /// <param name="configure">Configures the child mapping.</param>
    /// <param name="objectMissing">Handles a changed-to-missing nested object.</param>
    /// <typeparam name="TDtoChild">The nested DTO type.</typeparam>
    /// <typeparam name="TEntityChild">The navigation target type.</typeparam>
    /// <returns>This builder.</returns>
    public SparseEntityMapBuilder<TEntity, TDto> Object<TDtoChild, TEntityChild>(
        Expression<Func<TDto, TDtoChild?>> dto,
        Expression<Func<TEntity, TEntityChild?>> entity,
        Action<SparseEntityMapBuilder<TEntityChild, TDtoChild>> configure,
        SparseEntityMissingPolicy objectMissing = SparseEntityMissingPolicy.Throw
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        ArgumentNullException.ThrowIfNull(configure);
        var child = new SparseEntityMapBuilder<TEntityChild, TDtoChild>
        {
            DefaultMissing = DefaultMissing,
        };
        configure(child);
        AddNode(
            new ObjectNode<TDto, TEntity, TDtoChild, TEntityChild>(
                dto,
                entity,
                child,
                (session, target) =>
                    EntityPlan<TEntityChild, TDtoChild>.Build(session, child, target),
                objectMissing
            )
        );
        return this;
    }

    /// <summary>
    /// Marks a DTO member as a collection path.
    /// </summary>
    /// <remarks>
    /// This proof of concept does not synchronize collections: any change under
    /// a marked path fails with
    /// <see cref="SparseEntityMappingFailure.CollectionNotSupported" /> and points
    /// at issue #225. Element-level configuration arrives with that issue.
    /// </remarks>
    /// <param name="dto">Selects the DTO collection member (single level).</param>
    /// <param name="entity">Selects the entity collection (single level).</param>
    /// <typeparam name="TDtoValue">The DTO member type.</typeparam>
    /// <typeparam name="TEntityValue">The entity member type.</typeparam>
    /// <returns>This builder.</returns>
    public SparseEntityMapBuilder<TEntity, TDto> Collection<TDtoValue, TEntityValue>(
        Expression<Func<TDto, TDtoValue?>> dto,
        Expression<Func<TEntity, TEntityValue?>> entity
    )
    {
        var dtoName = MemberPath.Parse(dto).SingleName("DTO");
        var entityName = MemberPath.Parse(entity).SingleName("entity");
        AddCollection(dtoName, entityName);
        return this;
    }

    /// <summary>
    /// Maps a keyed child collection with path-scoped element configuration.
    /// </summary>
    /// <remarks>
    /// The child mapping, key projection, factory, removal policy, and order
    /// setter declared in <paramref name="configure" /> apply to this parent
    /// collection only. Dispatch runs through the typed keyed transition
    /// emitted by the ordinary <c>SparseFragments.Generator</c>; no separate
    /// EF generator exists. An explicitly configured DTO path replaces its
    /// inferred default instead of running both.
    /// </remarks>
    /// <param name="dto">Selects the DTO collection member.</param>
    /// <param name="entity">Selects the entity collection navigation.</param>
    /// <param name="configure">Configures the scoped child mapping and policies.</param>
    /// <typeparam name="TDtoChild">The DTO element type.</typeparam>
    /// <typeparam name="TEntityChild">The entity element type.</typeparam>
    /// <returns>This builder.</returns>
    public SparseEntityMapBuilder<TEntity, TDto> Collection<TDtoChild, TEntityChild>(
        Expression<Func<TDto, IEnumerable<TDtoChild>?>> dto,
        Expression<Func<TEntity, IEnumerable<TEntityChild>?>> entity,
        Action<SparseEntityCollectionBuilder<TEntityChild, TDtoChild>> configure
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(configure);
        var lines = new SparseEntityCollectionBuilder<TEntityChild, TDtoChild>();
        configure(lines);
        var dtoName = MemberPath.Parse(dto).SingleName("DTO");
        AddNode(
            new CollectionNode<TDto, TEntity, TDtoChild, TEntityChild>(
                dto,
                entity,
                new CollectionDeclaration<TDtoChild, TEntityChild>(
                    lines.ElementConfigure
                        ?? throw new SparseEntityMappingException(
                            SparseEntityMappingFailure.MetadataMismatch,
                            dtoName,
                            "The collection declares no Element() child mapping. Scoped child overrides live in the same parent mapper Configure."
                        ),
                    lines.CreateFactory,
                    lines.RemoveKind,
                    lines.RemoveHandler,
                    lines.SoftDeleteMark,
                    lines.OrderSetter,
                    lines.KeyType,
                    lines.DtoKeySelector,
                    lines.EntityKeySelector
                )
            )
        );
        return this;
    }

    /// <summary>
    /// Sets the missing-value policy for one mapped scalar member.
    /// </summary>
    /// <remarks>
    /// A per-member OnMissing call refines the policy passed to a Member overload.
    /// Global defaults from MissingDefault are checked lazily: a default that a
    /// member cannot honor fails only when that member actually goes missing.
    /// </remarks>
    /// <param name="dto">Selects the DTO member (single level).</param>
    /// <param name="policy">Handles a changed-to-missing after-state for this member.</param>
    /// <typeparam name="TValue">The DTO member type.</typeparam>
    /// <returns>This builder.</returns>
    public SparseEntityMapBuilder<TEntity, TDto> OnMissing<TValue>(
        Expression<Func<TDto, TValue?>> dto,
        SparseEntityMissingPolicy policy
    )
    {
        AddMissingOverride(MemberPath.Parse(dto).SingleName("DTO"), policy);
        return this;
    }

    /// <summary>
    /// Allows writing a DTO member that targets an identity, generated,
    /// concurrency-token, or shadow member.
    /// </summary>
    /// <param name="dto">Selects the DTO member (single level).</param>
    /// <typeparam name="TValue">The DTO member type.</typeparam>
    /// <returns>This builder.</returns>
    public SparseEntityMapBuilder<TEntity, TDto> AllowWrite<TValue>(
        Expression<Func<TDto, TValue?>> dto
    )
    {
        AddAllowedWrite(MemberPath.Parse(dto).SingleName("DTO"));
        return this;
    }

    /// <summary>
    /// Sets the default missing-value policy for this scope.
    /// </summary>
    /// <param name="policy">Handles changed-to-missing after-states without a per-member policy.</param>
    /// <returns>This builder.</returns>
    public SparseEntityMapBuilder<TEntity, TDto> MissingDefault(SparseEntityMissingPolicy policy)
    {
        DefaultMissing = policy;
        return this;
    }
}

/// <summary>
/// Non-generic registration store shared by every builder scope.
/// </summary>
/// <remarks>
/// Nested child builders are different closed generics; the plan builder works
/// against this base so recursion needs no runtime reflection.
/// </remarks>
public abstract class SparseEntityMapBuilderBase
{
    private readonly List<MemberNode> _nodes = [];
    private readonly HashSet<string> _nodeNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ignored = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _collections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SparseEntityMissingPolicy> _missingOverrides = new(
        StringComparer.Ordinal
    );
    private readonly HashSet<string> _allowedWrites = new(StringComparer.Ordinal);

    /// <summary>Gets or sets the default missing-value policy for this scope.</summary>
    public SparseEntityMissingPolicy DefaultMissing { get; protected set; } =
        SparseEntityMissingPolicy.Throw;

    internal IReadOnlyList<MemberNode> Nodes => _nodes;

    internal IReadOnlySet<string> Ignored => _ignored;

    internal IReadOnlyDictionary<string, string> Collections => _collections;

    internal IReadOnlyDictionary<string, SparseEntityMissingPolicy> MissingOverrides =>
        _missingOverrides;

    internal IReadOnlySet<string> AllowedWrites => _allowedWrites;

    internal void AddNode(MemberNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!_nodeNames.Add(node.DtoName))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.Duplicate,
                node.DtoName,
                $"The DTO member '{node.DtoName}' is already mapped. Each DTO path replaces its default once."
            );
        }

        _nodes.Add(node);
    }

    internal void AddIgnored(string dtoName)
    {
        if (!_nodeNames.Add(dtoName))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.Duplicate,
                dtoName,
                $"The DTO member '{dtoName}' is already mapped."
            );
        }

        _ignored.Add(dtoName);
    }

    internal void AddCollection(string dtoName, string entityName)
    {
        if (!_nodeNames.Add(dtoName))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.Duplicate,
                dtoName,
                $"The DTO member '{dtoName}' is already mapped."
            );
        }

        _collections.Add(dtoName, entityName);
    }

    internal void AddMissingOverride(string dtoName, SparseEntityMissingPolicy policy)
    {
        _missingOverrides[dtoName] = policy;
    }

    internal void AddAllowedWrite(string dtoName)
    {
        _allowedWrites.Add(dtoName);
    }

    internal void InheritDefaultMissing(SparseEntityMissingPolicy policy)
    {
        DefaultMissing = policy;
    }

    internal bool Covers(string dtoName) => _nodeNames.Contains(dtoName);
}
