using System.Collections.Generic;

namespace SparseFragments.Generator.Shared;

/// <summary>Central semantic-role reference resolver for generated family names.</summary>
/// <remarks>
/// <para>
/// Emitters must not rebuild product type names from literals such as
/// <c>element.NonNullableName + ".Fragment"</c> or by stripping a
/// <c>"Fragment"</c> suffix to append <c>"Patch"</c>. Those constructions
/// bind shared model-shape and algebra generation to one product's public
/// vocabulary. All child and nested references flow through this resolver,
/// which derives them from the owning generator's
/// <see cref="SparseFamilyNames"/> bindings.
/// </para>
/// <para>
/// Root declarations keep their current emission until the emission
/// infrastructure track threads families through every emitter; this
/// resolver owns the reference side (child fragment, patch, change-set,
/// and observable names plus UI collision handling) so references stay
/// coherent with any product vocabulary today.
/// </para>
/// </remarks>
internal static class SparseSemanticReference
{
    /// <summary>Derives the child state (fragment-role) type name for a host type name.</summary>
    /// <param name="host">Containing type name for the child model.</param>
    /// <param name="family">Product family-name bindings.</param>
    /// <returns>The qualified child state type name.</returns>
    public static string ChildFragmentType(string host, SparseFamilyNames family) =>
        host + "." + family.Fragment;

    /// <summary>Derives the child operation (patch-role) type name from a child state type name.</summary>
    /// <remarks>
    /// Replaces a trailing state-role suffix with the operation-role name;
    /// when the input carries no such suffix (for example a hand-mapped
    /// downstream name), the operation name is appended instead of
    /// fabricating a suffix strip.
    /// </remarks>
    /// <param name="childFragmentType">Child state type name.</param>
    /// <param name="family">Product family-name bindings.</param>
    /// <returns>The child operation type name.</returns>
    public static string ChildPatchType(string childFragmentType, SparseFamilyNames family) =>
        ReplaceRoleSuffix(childFragmentType, family.Fragment, family.Patch);

    /// <summary>Derives the child transition (change-set-role) type name from a child state type name.</summary>
    /// <param name="childFragmentType">Child state type name.</param>
    /// <param name="family">Product family-name bindings.</param>
    /// <returns>The child transition type name.</returns>
    public static string ChildChangeSetType(string childFragmentType, SparseFamilyNames family) =>
        ReplaceRoleSuffix(childFragmentType, family.Fragment, family.ChangeSet);

    /// <summary>Derives the child observable-view type name.</summary>
    /// <remarks>
    /// A discovered per-model view name (already collision-resolved at
    /// analysis) wins; otherwise the observable role name replaces the
    /// state-role suffix on the child state type, mirroring the patch and
    /// change-set derivations.
    /// </remarks>
    /// <param name="host">Containing type name for the child model.</param>
    /// <param name="childFragmentType">Child state type name.</param>
    /// <param name="discoveredViewName">Collision-resolved view name from analysis, if any.</param>
    /// <param name="family">Product family-name bindings.</param>
    /// <returns>The child observable-view type name.</returns>
    public static string ChildObservableType(
        string host,
        string childFragmentType,
        string? discoveredViewName,
        SparseFamilyNames family
    ) =>
        discoveredViewName is not null
            ? host + "." + discoveredViewName
            : ReplaceRoleSuffix(childFragmentType, family.Fragment, family.Observable);

    /// <summary>Resolves a collision-aware observable root name for one member set.</summary>
    /// <param name="memberNames">Source member names competing for the name.</param>
    /// <param name="family">Product family-name bindings.</param>
    /// <returns>The usable observable root name.</returns>
    public static string ObservableRootName(
        IEnumerable<string> memberNames,
        SparseFamilyNames family
    ) => SparseGeneratedPlacement.ResolveUiTypeName(family.Observable, memberNames);

    /// <summary>Resolves a collision-aware read-only-view root name for one member set.</summary>
    /// <param name="memberNames">Source member names competing for the name.</param>
    /// <param name="family">Product family-name bindings.</param>
    /// <returns>The usable read-only-view root name.</returns>
    public static string ReadOnlyViewRootName(
        IEnumerable<string> memberNames,
        SparseFamilyNames family
    ) => SparseGeneratedPlacement.ResolveUiTypeName(family.ReadOnlyView, memberNames);

    private static string ReplaceRoleSuffix(string typeName, string fromRole, string toRole)
    {
        var suffix = "." + fromRole;
        return typeName.EndsWith(suffix, System.StringComparison.Ordinal)
            ? typeName.Substring(0, typeName.Length - fromRole.Length) + toRole
            : typeName + "." + toRole;
    }
}
