namespace SparseFragments.EntityFrameworkCore.Mapping;

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
/// default exactly once at the same DTO path. When no companion exists (a
/// compilation without the generator), the scoped runtime inference in
/// <c>EntityPlan</c> still validates.
/// </remarks>
internal static class GeneratedDefaults
{
    /// <summary>
    /// Applies generated defaults for one mapping instance when emitted.
    /// </summary>
    /// <returns>True when a companion ran; otherwise false.</returns>
    public static bool TryApply<TEntity, TDto>(
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
            return false;
        }

        var register = companion.GetMethod(
            "RegisterDefaults",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
        );
        if (register is null)
        {
            return false;
        }

        var parameters = register.GetParameters();
        if (parameters.Length != 1 || !parameters[0].ParameterType.IsInstanceOfType(builder))
        {
            return false;
        }

        builder.GeneratedRegistrationMode = true;
        try
        {
            register.Invoke(null, [builder]);
        }
        finally
        {
            builder.GeneratedRegistrationMode = false;
        }

        builder.GeneratedDefaultsApplied = true;
        return true;
    }
}
