using System.Linq.Expressions;

namespace SparseFragments.EntityFrameworkCore.Mapping;

/// <summary>
/// A keyed child collection synchronized by stable DTO keys.
/// </summary>
/// <remarks>
/// Dispatch runs through the typed keyed transition emitted by the ordinary
/// <c>SparseFragments.Generator</c> (closed once at plan build, never
/// re-discovered per apply). Added children recurse through the path-scoped
/// child plan built from this collection's <c>Element</c> configuration;
/// edited children recurse through their typed element transitions. The child
/// plan is scoped to this parent path: the same element CLR types under
/// another collection build an independent plan.
/// </remarks>
internal sealed class CollectionNode<TDto, TEntity, TDtoChild, TEntityChild> : MemberNode
    where TDto : class
    where TEntity : class
    where TDtoChild : class
    where TEntityChild : class
{
    private readonly Expression<Func<TDto, IEnumerable<TDtoChild>?>> _dtoSelector;
    private readonly Expression<Func<TEntity, IEnumerable<TEntityChild>?>> _entitySelector;
    private readonly CollectionDeclaration<TDtoChild, TEntityChild> _declaration;
    private CollectionRuntime<TDtoChild, TEntityChild>? _runtime;

    public CollectionNode(
        Expression<Func<TDto, IEnumerable<TDtoChild>?>> dto,
        Expression<Func<TEntity, IEnumerable<TEntityChild>?>> entity,
        CollectionDeclaration<TDtoChild, TEntityChild> declaration
    )
        : base(MemberPath.Parse(dto).SingleName("DTO"))
    {
        _dtoSelector = dto;
        _entitySelector = entity;
        _declaration = declaration;
    }

    public override void Resolve(PlanBuildSession session, PlanLevel level)
    {
        _runtime = CollectionRuntime<TDtoChild, TEntityChild>.Create<TDto, TEntity>(
            DtoName,
            _dtoSelector,
            _entitySelector,
            _declaration,
            session,
            level
        );
    }

    public override void Prepare(PlanApplyState state, List<Action> actions)
    {
        Runtime.Prepare(state, actions);
    }

    public override void WriteAll(PlanWriteState state, List<Action> actions)
    {
        Runtime.WriteAll(state, actions);
    }

    private CollectionRuntime<TDtoChild, TEntityChild> Runtime =>
        _runtime ?? throw new InvalidOperationException("The collection plan was not resolved.");
}
