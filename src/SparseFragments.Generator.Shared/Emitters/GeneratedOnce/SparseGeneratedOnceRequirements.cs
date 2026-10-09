using System.Collections.Immutable;

namespace SparseFragments.Generator.Shared;

/// <summary>Capability aggregation for Generated-Once helpers (track-3).</summary>
/// <remarks>
/// Interim seam for #178: the product generator collects these flags across
/// explicit and promoted models and emits one shared source per family.
/// #178 should replace the manual aggregation with its feature plan while
/// keeping the helper identities in <see cref="SparseGeneratedOnceNames"/>.
/// </remarks>
internal sealed record SparseGeneratedOnceRequirements(
    bool NeedsReadOnlyCollection,
    bool NeedsReadOnlyDictionary,
    bool NeedsReadOnlyEntries,
    bool NeedsCloneKernels = false,
    bool NeedsPortableSetView = false,
    bool NeedsRemovalIndex = false
)
{
    /// <summary>Empty requirements: no shared helper is needed.</summary>
    public static SparseGeneratedOnceRequirements Empty { get; } = new(false, false, false);

    /// <summary>Whether any read-only adapter is required.</summary>
    public bool NeedsReadOnlyAdapters =>
        NeedsReadOnlyCollection || NeedsReadOnlyDictionary || NeedsReadOnlyEntries;

    /// <summary>Combines two requirement sets.</summary>
    /// <param name="other">Other set.</param>
    /// <returns>Union of both sets.</returns>
    public SparseGeneratedOnceRequirements Union(SparseGeneratedOnceRequirements other) =>
        new(
            NeedsReadOnlyCollection || other.NeedsReadOnlyCollection,
            NeedsReadOnlyDictionary || other.NeedsReadOnlyDictionary,
            NeedsReadOnlyEntries || other.NeedsReadOnlyEntries,
            NeedsCloneKernels || other.NeedsCloneKernels,
            NeedsPortableSetView || other.NeedsPortableSetView,
            NeedsRemovalIndex || other.NeedsRemovalIndex
        );

    /// <summary>Computes read-only adapter needs for one model.</summary>
    /// <param name="members">Model members.</param>
    /// <param name="readOnlyViewModels">POCO read-only views.</param>
    /// <returns>Requirements for the model.</returns>
    public static SparseGeneratedOnceRequirements ForReadOnlyView(
        ImmutableArray<SparseMemberModel> members,
        ImmutableArray<SparseReadOnlyViewModel> readOnlyViewModels
    )
    {
        // LINQ keeps the analyzer's S3267 quiet and states the capability
        // query directly: sequences need the list adapter, dictionaries need
        // all three (Keys reuses the list adapter; key mapping may need entries).
        var needsCollection =
            members.Any(static member => IsSequence(member))
            || members.Any(static member => member.Collection.ValueType is not null);
        var needsDictionary = members.Any(static member => member.Collection.ValueType is not null);
        var needsEntries = needsDictionary;

        if (!readOnlyViewModels.IsDefault)
        {
            foreach (var view in readOnlyViewModels)
            {
                var nested = ForReadOnlyView(view.Members, default);
                needsCollection |= nested.NeedsReadOnlyCollection;
                needsDictionary |= nested.NeedsReadOnlyDictionary;
                needsEntries |= nested.NeedsReadOnlyEntries;
            }
        }

        return new(needsCollection, needsDictionary, needsEntries);
    }

    private static bool IsSequence(SparseMemberModel member) =>
        member.Collection.ElementType.Name is not null
        && member.Collection.Kind
            is SparseCollectionKind.Array
                or SparseCollectionKind.List
                or SparseCollectionKind.MutableList
                or SparseCollectionKind.Set;

    /// <summary>Computes clone kernel needs for one model.</summary>
    /// <remarks>
    /// The kernel family is emitted as a unit: array/list kernels call each
    /// other, so per-shape splitting would break cross-calls. The portable
    /// set bridge stays conditional on interface-set usage.
    /// </remarks>
    /// <param name="members">Model members.</param>
    /// <param name="pocoCloneModels">POCO clone models.</param>
    /// <returns>Requirements for the model.</returns>
    public static SparseGeneratedOnceRequirements ForCloneKernels(
        ImmutableArray<SparseMemberModel> members,
        ImmutableArray<SparsePocoCloneModel> pocoCloneModels
    )
    {
        var needsClone =
            members.Any(static member =>
                member.Collection.CloneKind != SparseCloneCollectionKind.Unsupported
            )
            || (
                !pocoCloneModels.IsDefault
                && pocoCloneModels.Any(static poco =>
                    poco.Members.Any(static member =>
                        member.Collection.CloneKind != SparseCloneCollectionKind.Unsupported
                    )
                )
            );
        var needsPortableView =
            needsClone
            && (
                members.Any(static member => IsInterfaceSet(member.Collection.NamedTypeDefinition))
                || (
                    !pocoCloneModels.IsDefault
                    && pocoCloneModels.Any(static poco =>
                        poco.Members.Any(static member =>
                            IsInterfaceSet(member.Collection.NamedTypeDefinition)
                        )
                    )
                )
            );
        return new(false, false, false, needsClone, needsPortableView);
    }

    /// <summary>Computes all Generated-Once needs for one model.</summary>
    /// <param name="members">Model members.</param>
    /// <param name="pocoCloneModels">POCO clone models.</param>
    /// <param name="readOnlyViewModels">POCO read-only views.</param>
    /// <param name="emitReadOnlyViews">Whether read-only views are emitted.</param>
    /// <returns>Combined requirements for the model.</returns>
    public static SparseGeneratedOnceRequirements ForModel(
        ImmutableArray<SparseMemberModel> members,
        ImmutableArray<SparsePocoCloneModel> pocoCloneModels,
        ImmutableArray<SparseReadOnlyViewModel> readOnlyViewModels,
        bool emitReadOnlyViews
    )
    {
        var readOnly = emitReadOnlyViews ? ForReadOnlyView(members, readOnlyViewModels) : Empty;
        return readOnly
            .Union(ForCloneKernels(members, pocoCloneModels))
            .Union(ForRemovalIndex(members));
    }

    /// <summary>Computes removal-index needs for one model.</summary>
    /// <remarks>
    /// Dictionary and keyed-sequence patches share the ordered-removal
    /// kernels; whole-replace members need none.
    /// </remarks>
    /// <param name="members">Model members.</param>
    /// <returns>Requirements for the model.</returns>
    public static SparseGeneratedOnceRequirements ForRemovalIndex(
        ImmutableArray<SparseMemberModel> members
    )
    {
        var needs = members.Any(static member =>
            !(member.HasExplicitMergeMode && member.MergeMode == SparseMergeModes.Replace)
            && (member.Collection.IsDictionary || member.Collection.IsKeyedSequence)
        );
        return new(false, false, false, NeedsRemovalIndex: needs);
    }

    private static bool IsInterfaceSet(string? namedTypeDefinition) =>
        namedTypeDefinition
            is SparseWellKnownNames.InterfaceSetTypeDefinition
                or SparseWellKnownNames.ReadOnlySetTypeDefinition;
}
