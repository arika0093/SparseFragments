using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SparseFragments.EntityFrameworkCore.Mapping;

namespace SparseFragments.EntityFrameworkCore;

/// <summary>Registers and validates generated EF entity mappers.</summary>
public static class SparseEntityMapperServiceCollectionExtensions
{
    /// <summary>
    /// Registers generated mappings and typed mapper services with singleton
    /// mapping instances by default.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="mappingTypes">
    /// Generated mapping types to register. When omitted, all mappings in the
    /// generated registry are registered.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentException">
    /// A requested type is not present in the generated mapper registry.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// More than one selected mapping handles the same entity and DTO types.
    /// </exception>
    public static IServiceCollection AddSparseEntityMappers(
        this IServiceCollection services,
        params Type[] mappingTypes
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(mappingTypes);

        var registrations = GeneratedDefaultsRegistry.GetRegistrations();
        var byMapperType = registrations.ToDictionary(static registration =>
            registration.MapperType
        );
        var selected = new List<GeneratedDefaultsRegistry.Registration>(
            mappingTypes.Length == 0 ? registrations.Count : mappingTypes.Length
        );
        if (mappingTypes.Length == 0)
        {
            selected.AddRange(registrations);
        }
        else
        {
            var selectedTypes = new HashSet<Type>();
            foreach (var mappingType in mappingTypes)
            {
                ArgumentNullException.ThrowIfNull(mappingType);
                if (!selectedTypes.Add(mappingType))
                {
                    continue;
                }

                if (!byMapperType.TryGetValue(mappingType, out var registration))
                {
                    throw new ArgumentException(
                        $"The mapping type '{mappingType}' is not present in the generated mapper registry.",
                        nameof(mappingTypes)
                    );
                }

                selected.Add(registration);
            }
        }

        var pairs = new HashSet<(Type EntityType, Type DtoType)>();
        if (
            selected.Any(registration =>
                !pairs.Add((registration.EntityType, registration.DtoType))
            )
        )
        {
            throw new InvalidOperationException(
                "More than one selected mapping handles the same entity and DTO types. Register one mapping type for each entity/DTO pair."
            );
        }

        selected.ForEach(registration =>
        {
            registration.AddServices(services);
            services.AddSingleton(
                new SparseEntityMapperValidationRegistration(
                    registration.EntityType,
                    registration.ValidatePlan
                )
            );
        });

        return services;
    }

    /// <summary>
    /// Builds and caches plans for registered mappings whose entity types occur
    /// in the specified context model.
    /// </summary>
    /// <typeparam name="TContext">The context whose model will be validated.</typeparam>
    /// <param name="serviceProvider">The root service provider.</param>
    public static void ValidateSparseEntityMappers<TContext>(this IServiceProvider serviceProvider)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        using var scope = serviceProvider.CreateScope();
        var scopedProvider = scope.ServiceProvider;
        var context = scopedProvider.GetRequiredService<TContext>();
        foreach (
            var registration in scopedProvider
                .GetServices<SparseEntityMapperValidationRegistration>()
                .Where(registration =>
                    context.Model.FindEntityType(registration.EntityType) is not null
                )
        )
        {
            registration.ValidatePlan(scopedProvider, context);
        }
    }

    internal static void AddMapper<TEntity, TDto>(IServiceCollection services, Type mapperType)
        where TEntity : class
        where TDto : class
    {
        services.TryAdd(ServiceDescriptor.Singleton(mapperType, mapperType));
        services.TryAdd(
            ServiceDescriptor.Singleton(
                typeof(ISparseEntityMapper<TEntity, TDto>),
                serviceProvider => new SparseEntityMapperService<TEntity, TDto>(
                    (IEntityModelMapping<TEntity, TDto>)
                        serviceProvider.GetRequiredService(mapperType)
                )
            )
        );
    }

    internal static void ValidatePlan<TEntity, TDto>(
        IServiceProvider serviceProvider,
        DbContext context
    )
        where TEntity : class
        where TDto : class
    {
        var mapper = serviceProvider.GetRequiredService<ISparseEntityMapper<TEntity, TDto>>();
        ((ISparseEntityMapperPlanCache)mapper).Validate(context);
    }
}

internal sealed record SparseEntityMapperValidationRegistration(
    Type EntityType,
    Action<IServiceProvider, DbContext> ValidatePlan
);
