using Microsoft.EntityFrameworkCore;
using SparseFragments.EntityFrameworkCore.Mapping;

namespace SparseFragments.EntityFrameworkCore;

/// <summary>Builds EF Core queries with the navigation paths needed by a change set.</summary>
public static class SparseEntityIncludeExtensions
{
    /// <summary>
    /// Adds the Include paths required to apply <paramref name="changes"/> with
    /// <paramref name="mapper"/>.
    /// </summary>
    /// <remarks>
    /// The query is not executed. Membership or order changes include complete
    /// collections; edit-only keyed transitions may use filtered Include when
    /// their entity key selector is a mapped scalar property. Other key shapes
    /// use an unfiltered Include. Owned navigations are omitted because EF loads
    /// them with their owner. Call the query provider's <c>AsSplitQuery</c>
    /// after this method when a split query is appropriate.
    /// </remarks>
    /// <param name="source">The root EF Core query.</param>
    /// <param name="mapper">The validated entity mapper for this query's model.</param>
    /// <param name="changes">The typed change set to apply.</param>
    /// <typeparam name="TEntity">The root entity type.</typeparam>
    /// <typeparam name="TDto">The mapped DTO type.</typeparam>
    /// <returns>The query with required Include paths applied.</returns>
    public static IQueryable<TEntity> IncludeFor<TEntity, TDto>(
        this IQueryable<TEntity> source,
        SparseEntityMapper<TEntity, TDto> mapper,
        ISparseChangeSet<TDto> changes
    )
        where TEntity : class
        where TDto : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.IsEmpty)
        {
            return source;
        }

        var collector = new IncludePathCollector();
        mapper.Plan.CollectIncludes(changes, collector, writeAll: false, afterSnapshot: null);
        return collector.Apply(source);
    }

    /// <summary>
    /// Adds the Include paths required to apply an untyped change set with
    /// <paramref name="mapper"/>.
    /// </summary>
    /// <param name="source">The root EF Core query.</param>
    /// <param name="mapper">The validated entity mapper for this query's model.</param>
    /// <param name="changes">The change set describing <typeparamref name="TDto"/>.</param>
    /// <typeparam name="TEntity">The root entity type.</typeparam>
    /// <typeparam name="TDto">The mapped DTO type.</typeparam>
    /// <returns>The query with required Include paths applied.</returns>
    /// <exception cref="ArgumentException">
    /// The change set does not describe <typeparamref name="TDto"/>.
    /// </exception>
    public static IQueryable<TEntity> IncludeFor<TEntity, TDto>(
        this IQueryable<TEntity> source,
        SparseEntityMapper<TEntity, TDto> mapper,
        ISparseChangeSet changes
    )
        where TEntity : class
        where TDto : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(changes);
        return changes is ISparseChangeSet<TDto> typed
            ? source.IncludeFor(mapper, typed)
            : throw new ArgumentException(
                $"The change set must describe DTO type '{typeof(TDto).FullName}'.",
                nameof(changes)
            );
    }
}
