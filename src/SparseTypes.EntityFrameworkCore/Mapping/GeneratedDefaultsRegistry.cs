using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SparseTypes.EntityFrameworkCore.Mapping;

/// <summary>Registers and resolves source-generated EF mapping defaults.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedDefaultsRegistry
{
    private static readonly ConcurrentDictionary<Type, Registration> Registrations = new();

    /// <summary>Registers defaults emitted for one mapper implementation.</summary>
    /// <typeparam name="TEntity">The entity type handled by the mapper.</typeparam>
    /// <typeparam name="TDto">The DTO type handled by the mapper.</typeparam>
    /// <param name="mapperType">The mapper implementation type.</param>
    /// <param name="registerDefaults">The generated defaults registration callback.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void Register<TEntity, TDto>(
        Type mapperType,
        Action<SparseEntityMapBuilder<TEntity, TDto>> registerDefaults
    )
        where TEntity : class
        where TDto : class
    {
        ArgumentNullException.ThrowIfNull(mapperType);
        ArgumentNullException.ThrowIfNull(registerDefaults);
        var registration = new Registration(
            mapperType,
            typeof(TEntity),
            typeof(TDto),
            builder => registerDefaults((SparseEntityMapBuilder<TEntity, TDto>)builder),
            services =>
                SparseEntityMapperServiceCollectionExtensions.AddMapper<TEntity, TDto>(
                    services,
                    mapperType
                ),
            static (serviceProvider, context) =>
                SparseEntityMapperServiceCollectionExtensions.ValidatePlan<TEntity, TDto>(
                    serviceProvider,
                    context
                )
        );
        if (!Registrations.TryAdd(mapperType, registration))
        {
            throw new InvalidOperationException(
                $"Generated defaults were registered more than once for '{mapperType}'."
            );
        }
    }

    internal static IReadOnlyCollection<Type> GetRegisteredMapperTypes() =>
        Registrations.Keys.ToArray();

    internal static IReadOnlyCollection<Registration> GetRegistrations() =>
        Registrations.Values.ToArray();

    internal static bool TryApply<TEntity, TDto>(
        Type mapperType,
        SparseEntityMapBuilder<TEntity, TDto> builder
    )
        where TEntity : class
        where TDto : class
    {
        if (!Registrations.TryGetValue(mapperType, out var registration))
        {
            return false;
        }

        if (registration.EntityType != typeof(TEntity) || registration.DtoType != typeof(TDto))
        {
            throw new InvalidOperationException(
                $"Generated defaults for '{mapperType}' target a different entity/DTO pair."
            );
        }

        builder.RunGenerated(() => registration.Apply(builder));
        builder.GeneratedDefaultsApplied = true;
        return true;
    }

    internal sealed record Registration(
        Type MapperType,
        Type EntityType,
        Type DtoType,
        Action<SparseEntityMapBuilderBase> Apply,
        Action<IServiceCollection> AddServices,
        Action<IServiceProvider, DbContext> ValidatePlan
    );
}
