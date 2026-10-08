using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SparseFragments.Generator.Shared;

/// <summary>Orchestrates model analysis: shape validation, member discovery and diagnostics.</summary>
/// <remarks>
/// Each analysis phase is owned by a focused component: <see cref="SparseModelDiscovery"/>
/// for member/model construction, <see cref="SparseCloneAnalysis"/> for cloneability and
/// cycles, and <see cref="SparseModelDiagnostics"/> for validation diagnostics.
/// </remarks>
internal static class SparseModelAnalyzer
{
    public static SparseGenerationAnalysis Analyze(
        INamedTypeSymbol model,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var location = model.Locations.FirstOrDefault();
        var declaration = model
            .DeclaringSyntaxReferences.Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault();

        switch (
            SparseShapeValidation.ValidateRootShape(model, declaration, config, cancellationToken)
        )
        {
            case SparseRootShapeProblem.MustBePartial:
                return Failure(config.EffectiveDiagnosticIds.MustBePartial, location, model.Name);
            case SparseRootShapeProblem.RefLikeModel:
            case SparseRootShapeProblem.FileLocalModel:
            case SparseRootShapeProblem.UnsupportedModel:
                return Failure(
                    config.EffectiveDiagnosticIds.UnsupportedModel,
                    location,
                    model.Name
                );
            case SparseRootShapeProblem.MissingConstructor:
                return Failure(
                    config.EffectiveDiagnosticIds.MissingConstructor,
                    location,
                    model.Name
                );
        }

        var members = SparseModelDiscovery
            .GetMembers(model, config, cancellationToken)
            .ToImmutableArray();
        var diagnostics = ImmutableArray.CreateBuilder<SparseGeneratorDiagnostic>();

        SparseModelDiagnostics.CollectMemberDiagnostics(
            model,
            members,
            config,
            diagnostics,
            cancellationToken
        );

        var memberModels = SparseModelDiscovery.CreateMemberModels(
            members,
            config,
            cancellationToken
        );

        // Formerly a late render-time check: reserved generated-name collisions
        // now fail during analysis on the shared collision primitive, keeping
        // the historical single generated-name diagnostic with no source location.
        var reservedCollision = SparseShapeValidation.FindFirstReservedNameCollision(
            memberModels,
            config.EffectiveReservedGeneratedNames
        );
        if (reservedCollision is not null)
        {
            diagnostics.Add(
                new SparseGeneratorDiagnostic(
                    config.EffectiveDiagnosticIds.GeneratedNameCollision,
                    null,
                    reservedCollision
                )
            );
        }

        // Statically provable duplicate JSON wire names fail here instead of
        // surfacing as runtime converter errors in generated code.
        foreach (var duplicate in SparseShapeValidation.FindDuplicateWireNames(memberModels))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var owner = SparseShapeValidation.FindWireNameOwner(
                members,
                duplicate,
                cancellationToken
            );
            diagnostics.Add(
                new SparseGeneratorDiagnostic(
                    config.EffectiveDiagnosticIds.DuplicateJsonPropertyName,
                    owner?.Locations.FirstOrDefault(),
                    duplicate
                )
            );
        }

        if (diagnostics.Count > 0)
        {
            return new SparseGenerationAnalysis(
                null,
                ImmutableArray<SparseMemberModel>.Empty,
                ImmutableArray<SparsePocoCloneModel>.Empty,
                ImmutableArray<SparseStructuralModel>.Empty,
                diagnostics.ToImmutable(),
                ImmutableArray<SparsePromotedModel>.Empty
            );
        }

        var fullyQualifiedName = model.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var hintName =
            SparseNaming.Sanitize(fullyQualifiedName, cancellationToken)
            + "_"
            + SparseNaming.GetStableTypeHash(fullyQualifiedName, cancellationToken)
            + config.HintNameSuffix;
        var pocoCloneModels = SparseModelDiscovery
            .GetPocoCloneTypes(members, config, cancellationToken)
            .Select(pocoType =>
                SparseModelDiscovery.CreatePocoCloneModel(pocoType, config, cancellationToken)
            )
            .ToImmutableArray();
        var structuralModels = SparseModelDiscovery
            .CollectStructuralTypes(members, config, cancellationToken)
            .Select(type =>
                SparseModelDiscovery.CreateStructuralModel(type, config, cancellationToken)
            )
            .ToImmutableArray();
        var promotedModels = SparseModelDiscovery.CreatePromotedModels(
            members,
            config,
            cancellationToken
        );
        var capabilityDiagnostics = ImmutableArray<SparseGeneratorDiagnostic>.Empty;
        if (
            !model.IsValueType
            && members.FirstOrDefault(static member =>
                member.Property.SetMethod is null || member.Property.SetMethod.IsInitOnly
            ) is { } immutableMember
        )
        {
            capabilityDiagnostics = ImmutableArray.Create(
                new SparseGeneratorDiagnostic(
                    "SPF026",
                    immutableMember.Property.Locations.FirstOrDefault(),
                    model.Name
                )
            );
        }

        return new SparseGenerationAnalysis(
            SparseModelDiscovery.CreateModelInfo(model, hintName, config, cancellationToken),
            memberModels,
            pocoCloneModels,
            structuralModels,
            capabilityDiagnostics,
            promotedModels
        );
    }

    private static SparseGenerationAnalysis Failure(
        string descriptorId,
        Location? location,
        string argument
    ) =>
        new(
            null,
            ImmutableArray<SparseMemberModel>.Empty,
            ImmutableArray<SparsePocoCloneModel>.Empty,
            ImmutableArray<SparseStructuralModel>.Empty,
            ImmutableArray.Create(new SparseGeneratorDiagnostic(descriptorId, location, argument)),
            ImmutableArray<SparsePromotedModel>.Empty
        );
}
