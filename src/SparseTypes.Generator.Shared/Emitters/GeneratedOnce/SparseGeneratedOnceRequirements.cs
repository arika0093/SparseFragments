using System.Collections.Immutable;

namespace SparseTypes.Generator.Shared;

/// <summary>Capability aggregation for Generated-Once helpers (track-3).</summary>
/// <remarks>The generator aggregates these flags across models and emits one source per helper family.</remarks>
internal sealed record SparseGeneratedOnceRequirements
{
    /// <summary>Whether the shared read-only list adapter is required.</summary>
    public bool NeedsReadOnlyCollection { get; init; }

    /// <summary>Whether the shared read-only dictionary adapter is required.</summary>
    public bool NeedsReadOnlyDictionary { get; init; }

    /// <summary>Whether the shared read-only dictionary-entries adapter is required.</summary>
    public bool NeedsReadOnlyEntries { get; init; }

    /// <summary>Whether the shared collection clone kernels are required.</summary>
    public bool NeedsCloneKernels { get; init; }

    /// <summary>Whether the portable set bridge is required.</summary>
    public bool NeedsPortableSetView { get; init; }

    /// <summary>Whether the shared ordered removal-index helper is required.</summary>
    public bool NeedsRemovalIndex { get; init; }

    /// <summary>Empty requirements: no shared helper is needed.</summary>
    public static SparseGeneratedOnceRequirements Empty { get; } =
        new SparseGeneratedOnceRequirements
        {
            NeedsReadOnlyCollection = false,
            NeedsReadOnlyDictionary = false,
            NeedsReadOnlyEntries = false,
            NeedsCloneKernels = false,
            NeedsPortableSetView = false,
            NeedsRemovalIndex = false,
        };

    /// <summary>Whether any read-only adapter is required.</summary>
    public bool NeedsReadOnlyAdapters =>
        NeedsReadOnlyCollection || NeedsReadOnlyDictionary || NeedsReadOnlyEntries;

    /// <summary>Combines two requirement sets.</summary>
    /// <param name="other">Other set.</param>
    /// <returns>Union of both sets.</returns>
    public SparseGeneratedOnceRequirements Union(SparseGeneratedOnceRequirements other) =>
        new SparseGeneratedOnceRequirements
        {
            NeedsReadOnlyCollection = NeedsReadOnlyCollection || other.NeedsReadOnlyCollection,
            NeedsReadOnlyDictionary = NeedsReadOnlyDictionary || other.NeedsReadOnlyDictionary,
            NeedsReadOnlyEntries = NeedsReadOnlyEntries || other.NeedsReadOnlyEntries,
            NeedsCloneKernels = NeedsCloneKernels || other.NeedsCloneKernels,
            NeedsPortableSetView = NeedsPortableSetView || other.NeedsPortableSetView,
            NeedsRemovalIndex = NeedsRemovalIndex || other.NeedsRemovalIndex,
        };

    /// <summary>Computes read-only adapter needs for one model.</summary>
    /// <param name="members">Model members.</param>
    /// <param name="readOnlyViewModels">POCO read-only views.</param>
    /// <returns>Requirements for the model.</returns>
    public static SparseGeneratedOnceRequirements ForReadOnlyView(
        ImmutableArray<SparseMemberModel> members,
        ImmutableArray<SparseReadOnlyViewModel> readOnlyViewModels
    )
    {
        // Sequences need the list adapter, dictionaries need all three (Keys
        // reuses the list adapter; key mapping may need entries).
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

        return new SparseGeneratedOnceRequirements
        {
            NeedsReadOnlyCollection = needsCollection,
            NeedsReadOnlyDictionary = needsDictionary,
            NeedsReadOnlyEntries = needsEntries,
            NeedsCloneKernels = false,
            NeedsPortableSetView = false,
            NeedsRemovalIndex = false,
        };
    }

    private static bool IsSequence(SparseMemberModel member) =>
        member.Collection.ElementType.Name is not null
        && member.Collection.Kind
            is SparseCollectionKind.Array
                or SparseCollectionKind.List
                or SparseCollectionKind.MutableList
                or SparseCollectionKind.Set;

    /// <summary>Computes clone kernel needs for one model.</summary>
    /// <remarks>The kernel family is emitted as a unit: array/list kernels call each other, so per-shape splitting would break cross-calls.</remarks>
    /// <param name="members">Model members.</param>
    /// <param name="pocoCloneModels">POCO clone models.</param>
    /// <param name="bclHashSetImplementsReadOnlySet">Whether the BCL set implements <c>IReadOnlySet{T}</c>.</param>
    /// <returns>Requirements for the model.</returns>
    public static SparseGeneratedOnceRequirements ForCloneKernels(
        ImmutableArray<SparseMemberModel> members,
        ImmutableArray<SparsePocoCloneModel> pocoCloneModels,
        bool bclHashSetImplementsReadOnlySet
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
        // Require the portable bridge only for IReadOnlySet members when the
        // target BCL lacks support; ISet-only models do not need it.
        var needsPortableView =
            !bclHashSetImplementsReadOnlySet
            && needsClone
            && (
                members.Any(static member => IsReadOnlySet(member.Collection.NamedTypeDefinition))
                || (
                    !pocoCloneModels.IsDefault
                    && pocoCloneModels.Any(static poco =>
                        poco.Members.Any(static member =>
                            IsReadOnlySet(member.Collection.NamedTypeDefinition)
                        )
                    )
                )
            );
        return new SparseGeneratedOnceRequirements
        {
            NeedsReadOnlyCollection = false,
            NeedsReadOnlyDictionary = false,
            NeedsReadOnlyEntries = false,
            NeedsCloneKernels = needsClone,
            NeedsPortableSetView = needsPortableView,
            NeedsRemovalIndex = false,
        };
    }

    /// <summary>Computes all Generated-Once needs for one model.</summary>
    /// <param name="members">Model members.</param>
    /// <param name="pocoCloneModels">POCO clone models.</param>
    /// <param name="readOnlyViewModels">POCO read-only views.</param>
    /// <param name="emitReadOnlyViews">Whether read-only views are emitted.</param>
    /// <param name="bclHashSetImplementsReadOnlySet">Whether the BCL set implements <c>IReadOnlySet{T}</c>.</param>
    /// <returns>Combined requirements for the model.</returns>
    public static SparseGeneratedOnceRequirements ForModel(
        ImmutableArray<SparseMemberModel> members,
        ImmutableArray<SparsePocoCloneModel> pocoCloneModels,
        ImmutableArray<SparseReadOnlyViewModel> readOnlyViewModels,
        bool emitReadOnlyViews,
        bool bclHashSetImplementsReadOnlySet
    )
    {
        var readOnly = emitReadOnlyViews ? ForReadOnlyView(members, readOnlyViewModels) : Empty;
        return readOnly
            .Union(ForCloneKernels(members, pocoCloneModels, bclHashSetImplementsReadOnlySet))
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
        return new SparseGeneratedOnceRequirements
        {
            NeedsReadOnlyCollection = false,
            NeedsReadOnlyDictionary = false,
            NeedsReadOnlyEntries = false,
            NeedsCloneKernels = false,
            NeedsPortableSetView = false,
            NeedsRemovalIndex = needs,
        };
    }

    private static bool IsReadOnlySet(string? namedTypeDefinition) =>
        namedTypeDefinition == SparseWellKnownNames.ReadOnlySetTypeDefinition;
}
