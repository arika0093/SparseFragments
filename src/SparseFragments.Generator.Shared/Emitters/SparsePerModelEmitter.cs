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

    /// <summary>Builds surface plus relocated operation implementation for one explicit root.</summary>
    /// <remarks>
    /// With an explicit implementation namespace the Patch/ChangeSet
    /// algorithms stream into the implementation source (issue #194) while
    /// the surface keeps thin facades; otherwise only the surface is
    /// produced. The implementation hint name resolves through
    /// <see cref="SparseGeneratedPlacement"/> with the configured suffix.
    /// </remarks>
    /// <returns>Surface source, implementation source (if relocated) and its hint name.</returns>
    public static (
        string Surface,
        string? Implementation,
        string? ImplementationHint
    ) BuildSplitSurface(
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
        {
            return (surface, null, null);
        }

        return (
            surface,
            implementation,
            SparseGeneratedPlacement.ImplementationHintName(
                model,
                config.EffectiveGeneratedImplementationSuffix,
                cancellationToken
            )
        );
    }

    /// <summary>Builds surface plus relocated implementation for one promoted model.</summary>
    /// <returns>Surface source, implementation source (if relocated) and its hint name.</returns>
    public static (
        string Surface,
        string? Implementation,
        string? ImplementationHint
    ) BuildPromotedSplitSurface(
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
        {
            return (surface, null, null);
        }

        return (
            surface,
            implementation,
            SparseGeneratedPlacement.ImplementationHintName(
                promoted.Model,
                config.EffectiveGeneratedImplementationSuffix,
                cancellationToken
            )
        );
    }
}
