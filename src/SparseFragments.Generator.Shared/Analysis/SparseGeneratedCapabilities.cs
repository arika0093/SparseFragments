using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace SparseFragments.Generator.Shared;

/// <summary>Model-independent helper families emitted once per compilation.</summary>
/// <remarks>
/// Each flag names one reusable helper family that does not depend on a
/// single model shape. Per-model callers reference these helpers instead of
/// re-emitting them, so a compilation holds exactly one definition.
/// </remarks>
[Flags]
internal enum SparseGeneratedCapability
{
    /// <summary>No shared helper is required.</summary>
    None = 0,

    /// <summary>Typed edit-session core over product runtime contracts.</summary>
    EditSession = 1 << 0,

    /// <summary>Generic collection clone helpers (including the portable set view variant).</summary>
    CloneHelpers = 1 << 1,

    /// <summary>Generic read-only streaming adapters.</summary>
    ReadOnlyAdapters = 1 << 2,

    /// <summary>Generic removal-index helpers for keyed and dictionary patches.</summary>
    RemovalIndex = 1 << 3,
}

/// <summary>Aggregated compilation-scoped emission plan.</summary>
/// <remarks>
/// Value equality keeps incremental output stable: unrelated model edits
/// that leave the plan unchanged reuse the cached helper sources, while a
/// changed capability set updates only the affected helper families.
/// </remarks>
internal sealed record SparseGeneratedOncePlan
{
    public SparseGeneratedOncePlan(
        SparseGeneratedCapability Capabilities,
        bool NeedsPortableSetView,
        bool HashSetSupportsCapacity
    )
    {
        this.Capabilities = Capabilities;
        this.NeedsPortableSetView = NeedsPortableSetView;
        this.HashSetSupportsCapacity = HashSetSupportsCapacity;
    }

    /// <summary>Requested helper families.</summary>
    public SparseGeneratedCapability Capabilities { get; init; }

    /// <summary>Whether the portable set-view variant is required (target-framework dependent).</summary>
    public bool NeedsPortableSetView { get; init; }

    /// <summary>Whether the target framework supports the HashSet capacity optimization.</summary>
    public bool HashSetSupportsCapacity { get; init; }

    /// <summary>Empty plan: no model requests a shared helper.</summary>
    public static SparseGeneratedOncePlan Empty { get; } =
        new(SparseGeneratedCapability.None, false, false);

    /// <summary>Whether the plan requests the given family.</summary>
    /// <param name="capability">Family to test.</param>
    /// <returns>True when the family is requested.</returns>
    public bool Has(SparseGeneratedCapability capability) =>
        (Capabilities & capability) == capability && capability != SparseGeneratedCapability.None;
}

/// <summary>Derives compilation-scoped helper requirements from analyzed models.</summary>
/// <remarks>
/// Requirements combine the product feature selection, the analyzed model
/// shape, the applicable collection families, and the target-framework
/// facts. Feature prerequisites are expanded and validated so a missing
/// prerequisite surfaces as an actionable diagnostic instead of missing code.
/// </remarks>
internal static class SparseGeneratedCapabilityPlanner
{
    /// <summary>Derives the helper families one analyzed root requires.</summary>
    /// <param name="analysis">Analyzed explicit root.</param>
    /// <param name="features">Product feature selection for the model.</param>
    /// <returns>The families the root requires.</returns>
    public static SparseGeneratedCapability ForAnalysis(
        SparseGenerationAnalysis analysis,
        SparseEmissionFeatures features
    )
    {
        if (!analysis.Model.HasValue)
        {
            return SparseGeneratedCapability.None;
        }

        return ForModel(
            analysis.Model.Value.IsStruct,
            analysis.Members,
            !analysis.PocoCloneModels.IsDefault && !analysis.PocoCloneModels.IsEmpty,
            !analysis.ReadOnlyViewModels.IsDefault && !analysis.ReadOnlyViewModels.IsEmpty,
            features
        );
    }

    /// <summary>Derives the helper families one promoted model requires.</summary>
    /// <param name="promoted">Deduplicated promoted model.</param>
    /// <param name="features">Product feature selection for the model.</param>
    /// <returns>The families the promoted model requires.</returns>
    public static SparseGeneratedCapability ForPromoted(
        SparsePromotedModel promoted,
        SparseEmissionFeatures features
    ) =>
        ForModel(
            promoted.Model.IsStruct,
            promoted.Members,
            !promoted.PocoCloneModels.IsDefault && !promoted.PocoCloneModels.IsEmpty,
            !promoted.ReadOnlyViewModels.IsDefault && !promoted.ReadOnlyViewModels.IsEmpty,
            features
        );

    /// <summary>Aggregates explicit and promoted requirements once for the compilation.</summary>
    /// <param name="analyses">Per-root analyses.</param>
    /// <param name="promoted">Deduplicated promoted models.</param>
    /// <param name="features">Product feature selection.</param>
    /// <param name="bclHashSetImplementsReadOnlySet">Target-framework set support.</param>
    /// <param name="bclHashSetSupportsCapacity">Target-framework capacity support.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>The aggregated plan.</returns>
    public static SparseGeneratedOncePlan Aggregate(
        ImmutableArray<SparseGenerationAnalysis> analyses,
        ImmutableArray<SparsePromotedModel> promoted,
        SparseEmissionFeatures features,
        bool bclHashSetImplementsReadOnlySet,
        bool bclHashSetSupportsCapacity,
        CancellationToken cancellationToken
    )
    {
        var capabilities = SparseGeneratedCapability.None;
        var needsPortableView = false;
        if (!analyses.IsDefault)
        {
            foreach (var analysis in analyses)
            {
                cancellationToken.ThrowIfCancellationRequested();
                capabilities |= ForAnalysis(analysis, features);
                needsPortableView |= RequiresPortableSetView(
                    analysis,
                    bclHashSetImplementsReadOnlySet
                );
            }
        }

        if (!promoted.IsDefault)
        {
            foreach (var model in promoted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                capabilities |= ForPromoted(model, features);
                needsPortableView |= RequiresPortableSetView(
                    model.Members,
                    model.PocoCloneModels,
                    bclHashSetImplementsReadOnlySet
                );
            }
        }

        // The portable view is a target-framework variant of the clone
        // family: it never appears without its owning family.
        if (needsPortableView)
        {
            capabilities |= SparseGeneratedCapability.CloneHelpers;
        }

        return new SparseGeneratedOncePlan(
            capabilities,
            needsPortableView && HasFlag(capabilities, SparseGeneratedCapability.CloneHelpers),
            bclHashSetSupportsCapacity
        );
    }

    /// <summary>Validates feature prerequisites for the aggregated plan.</summary>
    /// <remarks>
    /// Returns one actionable message per violation. Callers report these
    /// through the configured <c>InvalidEmissionPlan</c> diagnostic.
    /// </remarks>
    /// <param name="plan">Aggregated plan.</param>
    /// <param name="features">Product feature selection.</param>
    /// <returns>Validation messages, empty when coherent.</returns>
    public static ImmutableArray<string> Validate(
        SparseGeneratedOncePlan plan,
        SparseEmissionFeatures features
    )
    {
        var errors = ImmutableArray.CreateBuilder<string>();
        foreach (var dependency in features.ValidateDependencies())
        {
            errors.Add(dependency);
        }

        if (plan.Has(SparseGeneratedCapability.EditSession) && !features.EmitFragment)
        {
            errors.Add("Generated EditSession requires EmitFragment.");
        }

        if (plan.Has(SparseGeneratedCapability.CloneHelpers) && !features.EmitFragment)
        {
            errors.Add("Generated CloneHelpers requires EmitFragment.");
        }

        if (plan.Has(SparseGeneratedCapability.ReadOnlyAdapters) && !features.EmitObservable)
        {
            errors.Add("Generated ReadOnlyAdapters requires EmitObservable.");
        }

        if (plan.Has(SparseGeneratedCapability.RemovalIndex) && !features.EmitPatch)
        {
            errors.Add("Generated RemovalIndex requires EmitPatch.");
        }

        return errors.ToImmutable();
    }

    private static SparseGeneratedCapability ForModel(
        bool isStruct,
        ImmutableArray<SparseMemberModel> members,
        bool hasPocoClones,
        bool hasReadOnlyViews,
        SparseEmissionFeatures features
    )
    {
        var capabilities = SparseGeneratedCapability.None;
        if (members.IsDefault)
        {
            return capabilities;
        }

        // Edit sessions wrap reference-type models through the shared core.
        if (!isStruct && features.EmitFragment)
        {
            capabilities |= SparseGeneratedCapability.EditSession;
        }

        if (features.EmitFragment && (HasCloneableCollections(members) || hasPocoClones))
        {
            capabilities |= SparseGeneratedCapability.CloneHelpers;
        }

        if (
            features.EmitFragment
            && features.EmitObservable
            && (HasReadableCollections(members) || hasReadOnlyViews)
        )
        {
            capabilities |= SparseGeneratedCapability.ReadOnlyAdapters;
        }

        if (features.EmitPatch && HasKeyedOrDictionaryCollections(members))
        {
            capabilities |= SparseGeneratedCapability.RemovalIndex;
        }

        return capabilities;
    }

    private static bool HasCloneableCollections(ImmutableArray<SparseMemberModel> members) =>
        members
            .Select(static member => member.Collection)
            .Any(static collection =>
                collection.CloneKind != SparseCloneCollectionKind.Unsupported
            );

    private static bool HasReadableCollections(ImmutableArray<SparseMemberModel> members) =>
        members.Any(static member =>
            member.Collection.ValueType is not null
            || member.Collection.Kind
                is SparseCollectionKind.Array
                    or SparseCollectionKind.List
                    or SparseCollectionKind.MutableList
                    or SparseCollectionKind.Set
            || member.Property.Type.PocoReadOnlyViewKey is not null
            || member.ChildModel?.IsFragmentModel == true
        );

    private static bool HasKeyedOrDictionaryCollections(
        ImmutableArray<SparseMemberModel> members
    ) =>
        members
            .Select(static member => member.Collection)
            .Any(static collection => collection.IsKeyedSequence || collection.IsDictionary);

    private static bool RequiresPortableSetView(
        SparseGenerationAnalysis analysis,
        bool bclHashSetImplementsReadOnlySet
    )
    {
        if (bclHashSetImplementsReadOnlySet)
        {
            return false;
        }

        var definitions = CollectNamedTypeDefinitions(analysis.Members, analysis.PocoCloneModels);

        return SparseFragmentCollectionCloneEmitter.RequiresPortableSetView(
            bclHashSetImplementsReadOnlySet,
            definitions
        );
    }

    private static bool RequiresPortableSetView(
        ImmutableArray<SparseMemberModel> members,
        ImmutableArray<SparsePocoCloneModel> pocos,
        bool bclHashSetImplementsReadOnlySet
    )
    {
        if (bclHashSetImplementsReadOnlySet)
        {
            return false;
        }

        var definitions = CollectNamedTypeDefinitions(members, pocos);

        return SparseFragmentCollectionCloneEmitter.RequiresPortableSetView(
            bclHashSetImplementsReadOnlySet,
            definitions
        );
    }

    private static List<string?> CollectNamedTypeDefinitions(
        ImmutableArray<SparseMemberModel> members,
        ImmutableArray<SparsePocoCloneModel> pocos
    )
    {
        var definitions = new List<string?>();
        if (!members.IsDefault)
        {
            definitions.AddRange(
                members.Select(static member => member.Collection.NamedTypeDefinition)
            );
        }

        if (!pocos.IsDefault)
        {
            foreach (var pocoMembers in pocos.Select(static poco => poco.Members))
            {
                if (pocoMembers.IsDefault)
                {
                    continue;
                }

                definitions.AddRange(
                    pocoMembers.Select(static member => member.Collection.NamedTypeDefinition)
                );
            }
        }

        return definitions;
    }

    private static bool HasFlag(SparseGeneratedCapability value, SparseGeneratedCapability flag) =>
        (value & flag) == flag;
}
