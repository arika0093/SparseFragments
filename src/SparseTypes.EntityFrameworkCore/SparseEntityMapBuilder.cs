using System.Linq.Expressions;
using SparseTypes.EntityFrameworkCore.Mapping;

namespace SparseTypes.EntityFrameworkCore;

/// <summary>
/// Declares the mapping between one entity and one DTO type.
/// </summary>
/// <remarks>
/// Source-generated defaults (emitted by the ordinary generator for
/// <c>[SparseFragmentMapper]</c> types) are applied before
/// <c>Configure</c>; this builder registers overrides (Member overloads),
/// specialized writers (Write), nested scopes (Object), keyed collections
/// (Collection), ignores (Ignore), and policies (OnMissing, AllowWrite,
/// MissingDefault). An explicitly configured DTO path replaces its generated
/// default exactly once instead of running both.
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
    /// Registers an entity expression for reading one DTO member.
    /// </summary>
    /// <remarks>
    /// Use this when a write mapping has no inverse, when a member is ignored
    /// for writes, or when a value is produced by a custom writer.
    /// </remarks>
    /// <param name="dto">Selects the DTO member.</param>
    /// <param name="entity">Projects the entity value for that DTO member.</param>
    /// <typeparam name="TValue">The DTO member type.</typeparam>
    /// <returns>This builder.</returns>
    public SparseEntityMapBuilder<TEntity, TDto> Read<TValue>(
        Expression<Func<TDto, TValue?>> dto,
        Expression<Func<TEntity, TValue?>> entity
    )
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(entity);
        AddRead(MemberPath.Parse(dto).SingleName("DTO"), entity);
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
        // The child default is resolved at plan build, not here: a root
        // MissingDefault declared after this call still propagates unless the
        // child sets its own.
        var child = new SparseEntityMapBuilder<TEntityChild, TDtoChild>
        {
            GeneratedElementConfigurators = GeneratedElementConfigurators,
            GeneratedObjectConfigurators = GeneratedObjectConfigurators,
            GeneratedRegistrationMode = GeneratedRegistrationMode,
            IsNestedScope = true,
            // Companion-emitted Object scopes carry their own conventional
            // defaults, so the child plan skips runtime reflection scanning
            // just like element scopes do.
            GeneratedDefaultsApplied = GeneratedRegistrationMode,
        };
        var dtoName = MemberPath.Parse(dto).SingleName("DTO");
        if (GeneratedRegistrationMode)
        {
            GeneratedObjectConfigurators[(typeof(TEntity), typeof(TDto), dtoName)] = builder =>
                configure((SparseEntityMapBuilder<TEntityChild, TDtoChild>)builder);
        }
        else if (
            GeneratedObjectConfigurators.TryGetValue(
                (typeof(TEntity), typeof(TDto), dtoName),
                out var generatedObject
            )
        )
        {
            child.RunGenerated(() => generatedObject(child));
            child.GeneratedDefaultsApplied = true;
        }

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
    /// A marked path carries no synchronization behavior: any change under it
    /// fails with <see cref="SparseEntityMappingFailure.CollectionNotSupported" />.
    /// Use the keyed overload below for element mapping with identity, factory,
    /// removal, and order policies, <see cref="DeclareCollection{TDtoValue, TEntityValue}" />
    /// for the equivalent conventional marker, or <c>Ignore</c> to suppress it.
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
    /// emitted by the ordinary <c>SparseTypes.Generator</c>; no separate
    /// EF generator exists. An explicitly configured DTO path replaces its
    /// inferred default instead of running both. The scoped <c>Element</c>
    /// child mapping is optional: omit it when the source-generated
    /// conventional defaults already cover the child members.
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
        GeneratedElementConfigurators.TryGetValue(
            (typeof(TEntity), typeof(TDto), dtoName),
            out var generatedElement
        );
        AddNode(
            new CollectionNode<TDto, TEntity, TDtoChild, TEntityChild>(
                dto,
                entity,
                new CollectionDeclaration<TDtoChild, TEntityChild>(
                    lines.ElementConfigure,
                    lines.CreateFactory,
                    lines.RemoveKind,
                    lines.RemoveHandler,
                    lines.SoftDeleteMark,
                    lines.IsDeletedPredicate,
                    lines.IsDeletedExpression,
                    lines.OrderSetter,
                    lines.OrderSelectorExpression,
                    lines.KeyType,
                    lines.DtoKeySelector,
                    lines.EntityKeySelector,
                    lines.UnassignedKeyValue,
                    lines.HasUnassignedKeyValue,
                    generatedElement,
                    GeneratedElementConfigurators,
                    GeneratedObjectConfigurators
                )
            )
        );
        return this;
    }

    /// <summary>
    /// Declares a conventional collection path without synchronization behavior.
    /// </summary>
    /// <remarks>
    /// Emitted by the source-generated conventional defaults for same-name
    /// collection members and usable explicitly to mark a collection the mapper
    /// never writes. Any change under a declared-only path fails as
    /// <see cref="SparseEntityMappingFailure.CollectionNotSupported" />.
    /// Declare the full keyed <c>Collection(dto, entity, configure)</c> overload
    /// for the same path to replace this marker exactly once with path-scoped
    /// element mapping, identity, factory, removal, and order policies.
    /// </remarks>
    /// <param name="dto">Selects the DTO collection member (single level).</param>
    /// <param name="entity">Selects the entity collection (single level).</param>
    /// <typeparam name="TDtoValue">The DTO member type.</typeparam>
    /// <typeparam name="TEntityValue">The entity member type.</typeparam>
    /// <returns>This builder.</returns>
    public SparseEntityMapBuilder<TEntity, TDto> DeclareCollection<TDtoValue, TEntityValue>(
        Expression<Func<TDto, TDtoValue?>> dto,
        Expression<Func<TEntity, TEntityValue?>> entity
    )
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(entity);
        AddCollection(
            MemberPath.Parse(dto).SingleName("DTO"),
            MemberPath.Parse(entity).SingleName("entity")
        );
        return this;
    }

    /// <summary>
    /// Declares source-generated conventional element defaults for one collection.
    /// </summary>
    /// <remarks>
    /// Emitted by the source-generated companion for same-name collections
    /// with resolvable element types; explicit <c>Collection(dto, entity,
    /// configure)</c> entries compose these defaults underneath their own
    /// path-scoped <c>Element</c> configuration, which replaces each default
    /// exactly once. Identity, factory, removal, and order policies are never
    /// inferred and stay explicit.
    /// </remarks>
    /// <param name="dto">Selects the DTO collection member (single level).</param>
    /// <param name="configure">Registers the conventional child mapping.</param>
    /// <typeparam name="TDtoChild">The DTO element type.</typeparam>
    /// <typeparam name="TEntityChild">The entity element type.</typeparam>
    /// <returns>This builder.</returns>
    public SparseEntityMapBuilder<TEntity, TDto> DeclareCollectionElement<TDtoChild, TEntityChild>(
        Expression<Func<TDto, IEnumerable<TDtoChild>?>> dto,
        Action<SparseEntityMapBuilder<TEntityChild, TDtoChild>> configure
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(configure);
        GeneratedElementConfigurators[
            (typeof(TEntity), typeof(TDto), MemberPath.Parse(dto).SingleName("DTO"))
        ] = new GeneratedCollectionElementDefaults(builder =>
            configure((SparseEntityMapBuilder<TEntityChild, TDtoChild>)builder)
        );
        return this;
    }

    /// <summary>
    /// Declares generated child defaults and the key metadata used by keyed
    /// collection transitions.
    /// </summary>
    /// <param name="dto">Selects the DTO collection member.</param>
    /// <param name="key">Selects the generated stable DTO key.</param>
    /// <param name="unassignedKeyValue">The declared unassigned-key sentinel.</param>
    /// <param name="hasUnassignedKeyValue">Whether a sentinel was declared.</param>
    /// <param name="temporaryKey">Selects the generated temporary key, when present.</param>
    /// <param name="temporaryKeySetter">Assigns the generated temporary key, when present.</param>
    /// <param name="keyMemberNames">DTO members that contribute to stable identity.</param>
    /// <param name="configure">Registers the generated child defaults.</param>
    /// <typeparam name="TDtoChild">The DTO element type.</typeparam>
    /// <typeparam name="TEntityChild">The entity element type.</typeparam>
    /// <typeparam name="TKey">The stable DTO key type.</typeparam>
    /// <returns>This builder.</returns>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public SparseEntityMapBuilder<TEntity, TDto> DeclareCollectionElement<
        TDtoChild,
        TEntityChild,
        TKey
    >(
        Expression<Func<TDto, IEnumerable<TDtoChild>?>> dto,
        Expression<Func<TDtoChild, TKey>> key,
        object? unassignedKeyValue,
        bool hasUnassignedKeyValue,
        Expression<Func<TDtoChild, Guid?>>? temporaryKey,
        Action<TDtoChild, Guid?>? temporaryKeySetter,
        string[] keyMemberNames,
        Action<SparseEntityMapBuilder<TEntityChild, TDtoChild>> configure
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(keyMemberNames);
        ArgumentNullException.ThrowIfNull(configure);
        GeneratedElementConfigurators[
            (typeof(TEntity), typeof(TDto), MemberPath.Parse(dto).SingleName("DTO"))
        ] = new GeneratedCollectionElementDefaults(
            builder => configure((SparseEntityMapBuilder<TEntityChild, TDtoChild>)builder),
            typeof(TKey),
            key.Compile(),
            unassignedKeyValue,
            hasUnassignedKeyValue,
            temporaryKey?.Compile(),
            temporaryKeySetter,
            keyMemberNames
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
        MarkExplicitDefaultMissing();
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
    private readonly HashSet<string> _generatedDefaults = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ignored = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _collections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SparseEntityMissingPolicy> _missingOverrides = new(
        StringComparer.Ordinal
    );
    private readonly HashSet<string> _allowedWrites = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LambdaExpression> _reads = new(StringComparer.Ordinal);

    /// <summary>Gets or sets the default missing-value policy for this scope.</summary>
    public SparseEntityMissingPolicy DefaultMissing { get; protected set; } =
        SparseEntityMissingPolicy.Throw;

    internal IReadOnlyList<MemberNode> Nodes => _nodes;

    internal IReadOnlySet<string> Ignored => _ignored;

    internal IReadOnlyDictionary<string, string> Collections => _collections;

    internal IReadOnlyDictionary<string, SparseEntityMissingPolicy> MissingOverrides =>
        _missingOverrides;

    internal IReadOnlySet<string> AllowedWrites => _allowedWrites;

    internal IReadOnlyDictionary<string, LambdaExpression> Reads => _reads;

    /// <summary>
    /// Gets or sets whether registrations go through as source-generated
    /// conventional defaults. Set only while the generated companion runs.
    /// </summary>
    internal bool GeneratedRegistrationMode { get; set; }

    /// <summary>Gets whether a generated companion contributed defaults.</summary>
    internal bool GeneratedDefaultsApplied { get; set; }

    /// <summary>
    /// Gets the shared source-generated element configurators by parent scope.
    /// </summary>
    /// <remarks>
    /// Keyed by parent entity type, parent DTO type, and collection member
    /// name, so the same child type pair keeps one conventional default per
    /// parent collection while explicit <c>Element</c> overrides stay scoped.
    /// The reference is shared down the builder tree (nested objects and
    /// collection elements) as scopes are created.
    /// </remarks>
    internal Dictionary<
        (Type EntityType, Type DtoType, string Member),
        GeneratedCollectionElementDefaults
    > GeneratedElementConfigurators { get; set; } = new();

    internal Dictionary<
        (Type EntityType, Type DtoType, string Member),
        Action<SparseEntityMapBuilderBase>
    > GeneratedObjectConfigurators { get; set; } = new();

    internal HashSet<string> GeneratedKeyMembers { get; set; } = new(StringComparer.Ordinal);

    internal bool GeneratedKeyDeclared { get; set; }

    internal bool IsNestedScope { get; set; }

    /// <summary>Runs generated registrations with replacement semantics.</summary>
    internal void RunGenerated(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var previous = GeneratedRegistrationMode;
        GeneratedRegistrationMode = true;
        try
        {
            action();
        }
        finally
        {
            GeneratedRegistrationMode = previous;
        }
    }

    internal void AddNode(MemberNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (GeneratedRegistrationMode)
        {
            if (!_nodeNames.Add(node.DtoName))
            {
                return;
            }

            _generatedDefaults.Add(node.DtoName);
            _nodes.Add(node);
            return;
        }

        if (_nodeNames.Contains(node.DtoName))
        {
            // Explicit Configure replaces one generated default exactly once;
            // two explicit registrations for the same path stay a duplicate.
            if (_generatedDefaults.Remove(node.DtoName))
            {
                var name = node.DtoName;
                _nodes.RemoveAll(existing => existing.DtoName == name);
                _nodeNames.Remove(name);
                _ignored.Remove(name);
                _collections.Remove(name);
            }
            else
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.Duplicate,
                    node.DtoName,
                    $"The DTO member '{node.DtoName}' is already mapped. Each DTO path replaces its default once."
                );
            }
        }

        _nodeNames.Add(node.DtoName);
        _nodes.Add(node);
    }

    internal void AddIgnored(string dtoName)
    {
        if (GeneratedRegistrationMode)
        {
            if (!_nodeNames.Add(dtoName))
            {
                return;
            }

            _generatedDefaults.Add(dtoName);
            _ignored.Add(dtoName);
            return;
        }

        if (_nodeNames.Contains(dtoName))
        {
            if (_generatedDefaults.Remove(dtoName))
            {
                _nodes.RemoveAll(existing => existing.DtoName == dtoName);
                _nodeNames.Remove(dtoName);
                _collections.Remove(dtoName);
            }
            else
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.Duplicate,
                    dtoName,
                    $"The DTO member '{dtoName}' is already mapped."
                );
            }
        }
        else
        {
            _nodeNames.Add(dtoName);
        }

        _ignored.Add(dtoName);
    }

    internal void AddCollection(string dtoName, string entityName)
    {
        if (GeneratedRegistrationMode)
        {
            if (!_nodeNames.Add(dtoName))
            {
                return;
            }

            _generatedDefaults.Add(dtoName);
            _collections.Add(dtoName, entityName);
            return;
        }

        if (_nodeNames.Contains(dtoName))
        {
            if (_generatedDefaults.Remove(dtoName))
            {
                _nodes.RemoveAll(existing => existing.DtoName == dtoName);
                _nodeNames.Remove(dtoName);
                _ignored.Remove(dtoName);
                _collections.Remove(dtoName);
            }
            else
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.Duplicate,
                    dtoName,
                    $"The DTO member '{dtoName}' is already mapped."
                );
            }
        }
        else
        {
            _nodeNames.Add(dtoName);
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

    internal void AddRead(string dtoName, LambdaExpression entity)
    {
        ArgumentNullException.ThrowIfNull(dtoName);
        ArgumentNullException.ThrowIfNull(entity);
        if (!_reads.TryAdd(dtoName, entity))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.Duplicate,
                dtoName,
                $"The DTO member '{dtoName}' already has a Read() mapping."
            );
        }
    }

    internal void InheritDefaultMissing(SparseEntityMissingPolicy policy)
    {
        DefaultMissing = policy;
    }

    /// <summary>Gets whether this scope declared its own missing default.</summary>
    internal bool HasExplicitDefaultMissing { get; private set; }

    internal void MarkExplicitDefaultMissing() => HasExplicitDefaultMissing = true;

    internal bool Covers(string dtoName) => _nodeNames.Contains(dtoName);

    internal bool IsGeneratedDefault(string dtoName) => _generatedDefaults.Contains(dtoName);
}
