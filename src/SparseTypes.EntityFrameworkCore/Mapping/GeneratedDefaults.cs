namespace SparseTypes.EntityFrameworkCore.Mapping;

/// <summary>How source-generated conventional defaults resolved for one mapping.</summary>
internal enum GeneratedDefaultsStatus
{
    /// <summary>A companion ran and registered conventional defaults.</summary>
    Applied,

    /// <summary>No companion type was found for the mapping.</summary>
    MissingCompanion,

    /// <summary>A companion exists but exposes no compatible registration entry.</summary>
    IncompatibleCompanion,
}

/// <summary>
/// Applies source-generated conventional defaults emitted by the ordinary
/// <c>SparseTypes.Generator</c> for <c>[SparseFragmentMapper{TEntity, TDto}]</c> types.
/// </summary>
/// <remarks>
/// The generator discovers mapper Entity/DTO pairs (including DTOs in a
/// referenced Contracts assembly) and emits a companion type named
/// <c>{Mapper}_SparseEfDefaults</c> in the mapper's namespace with a static
/// <c>RegisterDefaults</c> method carrying source-level member selectors.
/// Reflection here only locates that explicitly registered companion; it never
/// scans DTO members for fallback implicit mapping. Registrations run in
/// generated-default mode so the generated bridge's explicit <c>Configure</c>
/// entries replace each default exactly once at the same DTO path.
/// </remarks>
internal static class GeneratedDefaults
{
    /// <summary>Gets whether a mapping opts into source-generated defaults.</summary>
    /// <remarks>
    /// True for <c>[SparseFragmentMapper{TEntity, TDto}]</c> mappings, whose
    /// companion must exist at mapper creation; otherwise creation fails fast
    /// instead of silently falling back to runtime inference.
    /// </remarks>
    /// <param name="mappingType">The mapper implementation type.</param>
    /// <returns>True when the mapper carries the marker attribute.</returns>
    public static bool RequiresGeneratedDefaults(Type mappingType)
    {
        ArgumentNullException.ThrowIfNull(mappingType);
        foreach (var attribute in mappingType.GetCustomAttributes(false))
        {
            var attributeType = attribute.GetType();
            if (
                attributeType.IsGenericType
                && attributeType.GetGenericTypeDefinition().FullName
                    == "SparseTypes.EntityFrameworkCore.SparseFragmentMapperAttribute`2"
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Applies generated defaults for one mapping instance when emitted.
    /// </summary>
    /// <returns>How the companion lookup resolved.</returns>
    public static GeneratedDefaultsStatus TryApply<TEntity, TDto>(
        IEntityModelMapping<TEntity, TDto> mapping,
        SparseEntityMapBuilder<TEntity, TDto> builder
    )
        where TEntity : class
        where TDto : class
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(builder);
        return GeneratedDefaultsRegistry.TryApply(mapping.GetType(), builder)
            ? GeneratedDefaultsStatus.Applied
            : GeneratedDefaultsStatus.MissingCompanion;
    }
}
