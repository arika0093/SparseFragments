using System;
using System.Collections.Immutable;
using System.Threading;

namespace SparseFragments.Generator.Shared;

/// <summary>Per-model emission path, separated from the compilation-scoped plane.</summary>
/// <remarks>
/// Per-model output holds type-safe state, transitions, and behavior for
/// one analyzed model. It may call into <see cref="SparseGeneratedOnceEmitter"/>
/// helpers but never re-emits them. Compilation-scoped helpers aggregate
/// separately through the capability plan.
/// </remarks>
internal static class SparsePerModelEmitter
{
    // The wide parameter list mirrors SparseFragmentEmitter.BuildSource
    // one-to-one: this type is a thin plane-separation seam, not a new API
    // surface, so folding parameters into a config object would only alias
    // the downstream signature. Keep the arity aligned instead.
    /// <summary>Builds the per-model surface source for one explicit root.</summary>
    /// <param name="model">Model identity.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="pocoCloneModels">POCO clone helpers.</param>
    /// <param name="readOnlyViewModels">Read-only view models.</param>
    /// <param name="structuralModels">Structural models.</param>
    /// <param name="bclHashSetImplementsReadOnlySet">Target-framework set support.</param>
    /// <param name="bclHashSetSupportsCapacity">Target-framework capacity support.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="appendProductExtensions">Product surface callback.</param>
    /// <returns>Per-model source; never a compilation-scoped helper.</returns>
    public static string BuildSurface(
        SparseModelInfo model,
        ImmutableArray<SparseMemberModel> members,
        ImmutableArray<SparsePocoCloneModel> pocoCloneModels,
        ImmutableArray<SparseReadOnlyViewModel> readOnlyViewModels,
        ImmutableArray<SparseStructuralModel> structuralModels,
        bool bclHashSetImplementsReadOnlySet,
        bool bclHashSetSupportsCapacity,
        CancellationToken cancellationToken,
        SparseGeneratorConfig config,
        Action<
            SharedIndentedBuilder,
            SparseModelInfo,
            ImmutableArray<SparseMemberModel>
        >? appendProductExtensions = null
    ) =>
        SparseFragmentEmitter.BuildSource(
            model,
            members,
            pocoCloneModels,
            readOnlyViewModels,
            structuralModels,
            bclHashSetImplementsReadOnlySet,
            bclHashSetSupportsCapacity,
            cancellationToken,
            config,
            appendProductExtensions
        );

    /// <summary>Builds relocated implementation sources for one explicit root.</summary>
    /// <param name="model">Model identity.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="readOnlyViewModels">Read-only view models.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <returns>Additional sources, or empty for single-file emission.</returns>
    public static ImmutableArray<SparseGeneratedSource> BuildImplementationSources(
        SparseModelInfo model,
        ImmutableArray<SparseMemberModel> members,
        ImmutableArray<SparseReadOnlyViewModel> readOnlyViewModels,
        CancellationToken cancellationToken,
        SparseGeneratorConfig config
    ) =>
        SparseFragmentEmitter.BuildImplementationSources(
            model,
            members,
            readOnlyViewModels,
            cancellationToken,
            config
        );

    /// <summary>Builds relocated implementation sources for one promoted model.</summary>
    /// <param name="promoted">Promoted model.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <returns>Additional sources, or empty for single-file emission.</returns>
    public static ImmutableArray<SparseGeneratedSource> BuildPromotedImplementationSources(
        SparsePromotedModel promoted,
        CancellationToken cancellationToken,
        SparseGeneratorConfig config
    ) =>
        SparseFragmentEmitter.BuildImplementationSources(
            promoted.Model,
            promoted.Members,
            promoted.ReadOnlyViewModels,
            cancellationToken,
            config
        );

    /// <summary>Builds the per-model surface source for one promoted model.</summary>
    /// <param name="promoted">Promoted model.</param>
    /// <param name="bclHashSetImplementsReadOnlySet">Target-framework set support.</param>
    /// <param name="bclHashSetSupportsCapacity">Target-framework capacity support.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <returns>Per-model source; never a compilation-scoped helper.</returns>
    public static string BuildPromotedSurface(
        SparsePromotedModel promoted,
        bool bclHashSetImplementsReadOnlySet,
        bool bclHashSetSupportsCapacity,
        CancellationToken cancellationToken,
        SparseGeneratorConfig config
    ) =>
        SparseFragmentEmitter.BuildPromotedSource(
            promoted,
            bclHashSetImplementsReadOnlySet,
            bclHashSetSupportsCapacity,
            cancellationToken,
            config
        );

    /// <summary>Builds split surface and implementation sources for one explicit root.</summary>
    /// <remarks>Union of the reloc-2 payload/Fragment-operations split (stage 3, #192)
    /// and the reloc-3 Patch/ChangeSet operation split (issue #194): the implementation
    /// source carries payload DTOs, Fragment operations/converter plus Patch/ChangeSet
    /// operations under the shared resolver hint with the configured suffix;
    /// null means legacy single-file emission.</remarks>
    public static (string Surface, SparseGeneratedSource? Implementation) BuildSplitSurface(
        SparseModelInfo model,
        ImmutableArray<SparseMemberModel> members,
        ImmutableArray<SparsePocoCloneModel> pocoCloneModels,
        ImmutableArray<SparseReadOnlyViewModel> readOnlyViewModels,
        ImmutableArray<SparseStructuralModel> structuralModels,
        bool bclHashSetImplementsReadOnlySet,
        bool bclHashSetSupportsCapacity,
        CancellationToken cancellationToken,
        SparseGeneratorConfig config,
        Action<
            SharedIndentedBuilder,
            SparseModelInfo,
            ImmutableArray<SparseMemberModel>
        >? appendProductExtensions = null
    )
    {
        var (surface, implementation) = SparseFragmentEmitter.BuildSplitSource(
            model,
            members,
            pocoCloneModels,
            readOnlyViewModels,
            structuralModels,
            bclHashSetImplementsReadOnlySet,
            bclHashSetSupportsCapacity,
            cancellationToken,
            config,
            appendProductExtensions
        );
        if (implementation is null)
            return (surface, null);
        var hint = SparseGeneratedPlacement.ImplementationHintName(
            model,
            config.EffectiveGeneratedImplementationSuffix,
            cancellationToken
        );
        return (surface, new SparseGeneratedSource(hint, implementation));
    }

    /// <summary>Builds split sources for one promoted model.</summary>
    public static (string Surface, SparseGeneratedSource? Implementation) BuildPromotedSplitSurface(
        SparsePromotedModel promoted,
        bool bclHashSetImplementsReadOnlySet,
        bool bclHashSetSupportsCapacity,
        CancellationToken cancellationToken,
        SparseGeneratorConfig config
    )
    {
        var (surface, implementation) = SparseFragmentEmitter.BuildPromotedSplitSource(
            promoted,
            bclHashSetImplementsReadOnlySet,
            bclHashSetSupportsCapacity,
            cancellationToken,
            config
        );
        if (implementation is null)
            return (surface, null);
        var hint = SparseGeneratedPlacement.ImplementationHintName(
            promoted.Model,
            config.EffectiveGeneratedImplementationSuffix,
            cancellationToken
        );
        return (surface, new SparseGeneratedSource(hint, implementation));
    }
}
