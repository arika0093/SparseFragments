namespace SparseFragments.EntityFrameworkCore.Mapping;

/// <summary>How source-generated conventional defaults resolved for one mapping.</summary>
public enum GeneratedDefaultsStatus
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
/// <c>SparseFragments.Generator</c> for <c>[SparseFragmentMapper]</c> types.
/// </summary>
/// <remarks>
/// The generator discovers mapper Entity/DTO pairs (including DTOs in a
/// referenced Contracts assembly) and emits a companion type named
/// <c>{Mapper}_SparseEfDefaults</c> in the mapper's namespace with a static
/// <c>RegisterDefaults</c> method carrying source-level member selectors.
/// Reflection here only locates that explicitly registered companion; it never
/// scans DTO members for fallback implicit mapping. Registrations run in
/// generated-default mode so an explicit <c>Configure</c> entry replaces each
/// default exactly once at the same DTO path. Mappings without the marker
/// attribute keep the scoped runtime inference fallback.
/// </remarks>
public static class GeneratedDefaults
{
    /// <summary>Gets whether a mapping opts into source-generated defaults.</summary>
    /// <remarks>
    /// True for <c>[SparseFragmentMapper]</c> mappings, whose companion must
    /// exist at mapper creation; otherwise creation fails fast instead of
    /// silently falling back to runtime inference.
    /// </remarks>
    /// <param name="mappingType">The mapper implementation type.</param>
    /// <returns>True when the mapper carries the marker attribute.</returns>
    public static bool RequiresGeneratedDefaults(Type mappingType)
    {
        ArgumentNullException.ThrowIfNull(mappingType);
        return mappingType.GetCustomAttributes(typeof(SparseFragmentMapperAttribute), false).Length
            > 0;
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
        var mappingType = mapping.GetType();
        var assembly = mappingType.Assembly;
        // Mirrors the emitter rule: enclosing type names joined with "_", so
        // nested mapper classes resolve their namespace-level companion.
        var nesting = new List<string>();
        for (
            var declaring = mappingType.DeclaringType;
            declaring is not null;
            declaring = declaring.DeclaringType
        )
        {
            nesting.Add(declaring.Name);
        }

        nesting.Reverse();
        nesting.Add(mappingType.Name);
        var companionName =
            (mappingType.Namespace is null ? string.Empty : mappingType.Namespace + ".")
            + string.Join("_", nesting)
            + "_SparseEfDefaults";
        var companion = assembly.GetType(companionName, throwOnError: false, ignoreCase: false);
        if (companion is null)
        {
            return GeneratedDefaultsStatus.MissingCompanion;
        }

        var register = companion.GetMethod(
            "RegisterDefaults",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
        );
        if (register is null)
        {
            return GeneratedDefaultsStatus.IncompatibleCompanion;
        }

        var parameters = register.GetParameters();
        if (parameters.Length != 1 || !parameters[0].ParameterType.IsInstanceOfType(builder))
        {
            return GeneratedDefaultsStatus.IncompatibleCompanion;
        }

        builder.RunGenerated(() => register.Invoke(null, [builder]));
        builder.GeneratedDefaultsApplied = true;
        return GeneratedDefaultsStatus.Applied;
    }
}
