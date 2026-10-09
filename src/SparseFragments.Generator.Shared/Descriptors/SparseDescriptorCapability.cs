using System.Collections.Immutable;

namespace SparseFragments.Generator.Shared;

/// <summary>Compilation-scoped descriptor helpers requested by analyzed models.</summary>
/// <remarks>
/// Static per-model metadata is emitted once per model and cached; generic
/// descriptor implementations (set, array/dictionary/set shapes, value
/// conversion) are shared once per compilation where contract identity
/// permits. Today the generic implementations live in the shared runtime
/// (once per assembly, which already de-duplicates); this capability records
/// the Generated-Once plan so the #178 pipeline can emit product-owned
/// variants without forking emitters.
/// </remarks>
[System.Flags]
internal enum SparseDescriptorCapability
{
    /// <summary>No descriptor helper is required.</summary>
    None = 0,

    /// <summary>Static per-model metadata caching is required.</summary>
    StaticMetadata = 1,

    /// <summary>Model-bound instance access bridges are required.</summary>
    InstanceBridges = 2,

    /// <summary>Model-specific change enumeration/projection is required.</summary>
    ChangeProjection = 4,

    /// <summary>Sequence descriptor helpers are required.</summary>
    ArrayHelpers = 8,

    /// <summary>Dictionary descriptor helpers are required.</summary>
    DictionaryHelpers = 16,

    /// <summary>Set descriptor helpers are required.</summary>
    SetHelpers = 32,

    /// <summary>Boxed value conversion helpers are required.</summary>
    ValueConversion = 64,
}

/// <summary>Capability aggregation for descriptor helpers.</summary>
/// <remarks>
/// Merge-time seam: the Generated-Once pipeline (#178) aggregates these flags
/// across models; the ownership design (#177) decides runtime versus
/// generated-once placement per helper family. Per-model static metadata keeps
/// using the #176 DescriptorFactory placement seam; compilation-scoped helper
/// hint names stay stable and unique through the configured dialect.
/// </remarks>
internal static class SparseDescriptorCapabilities
{
    /// <summary>Aggregates descriptor needs for one compilation.</summary>
    /// <param name="hasDescriptors">Whether any model emits descriptors.</param>
    /// <param name="hasCollections">Whether sequence/dictionary/set shapes occur.</param>
    /// <param name="hasChangeProjection">Whether change enumeration is needed.</param>
    /// <returns>The de-duplicated capability set.</returns>
    public static SparseDescriptorCapability ForCompilation(
        bool hasDescriptors,
        bool hasCollections,
        bool hasChangeProjection
    )
    {
        if (!hasDescriptors)
        {
            return SparseDescriptorCapability.None;
        }

        var capability =
            SparseDescriptorCapability.StaticMetadata
            | SparseDescriptorCapability.InstanceBridges
            | SparseDescriptorCapability.ValueConversion;
        if (hasCollections)
        {
            capability |=
                SparseDescriptorCapability.ArrayHelpers
                | SparseDescriptorCapability.DictionaryHelpers
                | SparseDescriptorCapability.SetHelpers;
        }

        if (hasChangeProjection)
        {
            capability |= SparseDescriptorCapability.ChangeProjection;
        }

        return capability;
    }

    /// <summary>Validates prerequisites for the requested helpers.</summary>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="capability">Requested capability set.</param>
    /// <returns>Actionable errors, empty when prerequisites hold.</returns>
    public static ImmutableArray<string> ValidatePrerequisites(
        SparseGeneratorConfig config,
        SparseDescriptorCapability capability
    )
    {
        var errors = ImmutableArray.CreateBuilder<string>();
        if (capability == SparseDescriptorCapability.None)
        {
            return errors.ToImmutable();
        }

        if (config.DescriptorDialect is null)
        {
            errors.Add("Descriptor helpers require SparseGeneratorConfig.DescriptorDialect.");
        }

        if (!config.EffectiveEmissionFeatures.EmitObservable)
        {
            errors.Add("Descriptor bridges require EmitObservable (live proxy access).");
        }

        return errors.ToImmutable();
    }
}
