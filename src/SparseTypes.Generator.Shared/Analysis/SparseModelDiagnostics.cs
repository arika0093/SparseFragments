using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace SparseTypes.Generator.Shared;

/// <summary>Collects validation diagnostics for discovered models and members.</summary>
internal static class SparseModelDiagnostics
{
    internal const int CustomMergeMode = SparseMergeModes.Custom;

    internal static void CollectMemberDiagnostics(
        INamedTypeSymbol model,
        ImmutableArray<SparseSymbolMemberModel> members,
        SparseGeneratorConfig config,
        ImmutableArray<SparseGeneratorDiagnostic>.Builder diagnostics,
        CancellationToken cancellationToken
    )
    {
        CollectSparseIgnoreDiagnostics(model, config, diagnostics, cancellationToken);

        foreach (
            var member in ModelConstructionPlan.UnsupportedRequiredMembers(
                model,
                members.Select(static member => member.Property),
                config,
                cancellationToken
            )
        )
            diagnostics.Add(
                new SparseGeneratorDiagnostic(
                    config.EffectiveDiagnosticIds.UnsupportedRequired,
                    member.Locations.FirstOrDefault(),
                    member.Name
                )
            );

        foreach (var property in UnsupportedStructuralMembers(model, config, cancellationToken))
            diagnostics.Add(
                new SparseGeneratorDiagnostic(
                    config.EffectiveDiagnosticIds.UnsupportedStructural,
                    property.Locations.FirstOrDefault(),
                    property.Name
                )
            );

        // Suppress downstream clone diagnostics when invalid key declarations
        // already provide the actionable error.
        var elementErrorProperties = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var member in members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member.Collection.ElementType is not INamedTypeSymbol element)
            {
                continue;
            }

            var elementDeclaresKey =
                SparseKeyAnalyzer.HasKeyDeclaration(element, config, cancellationToken)
                && !SparseKeyAnalyzer
                    .CollectDiagnostics(element, config, cancellationToken)
                    .IsEmpty;
            var elementDeclaresTemp =
                SparseTemporaryKeyAnalyzer.HasTemporaryKeyDeclaration(
                    element,
                    config,
                    cancellationToken
                )
                && SparseTemporaryKeyAnalyzer
                    .CollectDiagnostics(element, config, cancellationToken)
                    .Any(static diagnostic => !diagnostic.IsWarning);
            if (elementDeclaresKey || elementDeclaresTemp)
            {
                elementErrorProperties.Add(member.Property);
            }
        }

        var cloneReported = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (
            var property in SparseCloneAnalysis.UnsupportedCloneMembers(
                model,
                config,
                cancellationToken
            )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (elementErrorProperties.Contains(property))
            {
                continue;
            }

            cloneReported.Add(property);
            diagnostics.Add(
                new SparseGeneratorDiagnostic(
                    config.EffectiveDiagnosticIds.UnsupportedClone,
                    property.Locations.FirstOrDefault(),
                    property.Name
                )
            );
        }

        // Report unsupported member shapes here instead of surfacing generated
        // compilation errors; avoid duplicate diagnostics for prior failures.
        var unsupportedShapeProperties = members
            .Select(static member => member.Property)
            .Where(property =>
                !elementErrorProperties.Contains(property)
                && !cloneReported.Contains(property)
                && SparseShapeValidation.GetUnsupportedMemberReason(property.Type) is not null
            )
            .ToArray();
        foreach (var property in unsupportedShapeProperties)
        {
            cancellationToken.ThrowIfCancellationRequested();
            cloneReported.Add(property);
            diagnostics.Add(
                new SparseGeneratorDiagnostic(
                    config.EffectiveDiagnosticIds.UnsupportedClone,
                    property.Locations.FirstOrDefault(),
                    property.Name
                )
            );
        }

        foreach (var member in members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                member.MergeMode == CustomMergeMode
                && (
                    member.MergeStrategyType is null
                    || !SparseMergeValidation.IsValidCustomStrategy(
                        member.MergeStrategyType,
                        member.Property.Type,
                        member.ChildModel is not null,
                        config,
                        cancellationToken
                    )
                )
            )
            {
                diagnostics.Add(
                    new SparseGeneratorDiagnostic(
                        config.EffectiveDiagnosticIds.InvalidMergeStrategy,
                        member.Property.Locations.FirstOrDefault(),
                        member.Property.Name
                    )
                );
            }

            if (
                member.ComparisonComparerType is not null
                && !SparseComparisonValidation.IsValidComparer(
                    member.ComparisonComparerType,
                    member.Property.Type,
                    cancellationToken
                )
            )
            {
                diagnostics.Add(
                    new SparseGeneratorDiagnostic(
                        config.EffectiveDiagnosticIds.InvalidComparisonStrategy,
                        member.Property.Locations.FirstOrDefault(),
                        member.Property.Name
                    )
                );
            }

            if (
                SparseMergeValidation.GetUnsupportedReason(
                    member.MergeMode,
                    member.ChildModel is not null,
                    member.Collection.Kind
                )
                is not null
            )
            {
                diagnostics.Add(
                    new SparseGeneratorDiagnostic(
                        config.EffectiveDiagnosticIds.UnsupportedMerge,
                        member.Property.Locations.FirstOrDefault(),
                        member.Property.Name
                    )
                );
            }

            // Validate declared keys even when keyed merging is unused.
            var reportedKeyError = false;
            if (
                member.Collection.ElementType is INamedTypeSymbol keyedElement
                && SparseKeyAnalyzer.HasKeyDeclaration(keyedElement, config, cancellationToken)
            )
            {
                foreach (
                    var keyDiagnostic in SparseKeyAnalyzer.CollectDiagnostics(
                        keyedElement,
                        config,
                        cancellationToken
                    )
                )
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    diagnostics.Add(keyDiagnostic);
                    reportedKeyError = true;
                }
            }

            // Any declared [SparseTemporaryKey] must be well-shaped even when
            // the member takes whole-collection semantics and never uses it.
            if (
                member.Collection.ElementType is INamedTypeSymbol tempElement
                && SparseTemporaryKeyAnalyzer.HasTemporaryKeyDeclaration(
                    tempElement,
                    config,
                    cancellationToken
                )
            )
            {
                foreach (
                    var tempDiagnostic in SparseTemporaryKeyAnalyzer.CollectDiagnostics(
                        tempElement,
                        config,
                        cancellationToken
                    )
                )
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    diagnostics.Add(tempDiagnostic);
                    reportedKeyError = true;
                }
            }

            if (
                !reportedKeyError
                && SparseCollectionAnalyzer.IsUnkeyedStructuralSequence(
                    member,
                    config,
                    cancellationToken
                )
            )
            {
                diagnostics.Add(
                    new SparseGeneratorDiagnostic(
                        config.EffectiveDiagnosticIds.UnkeyedStructuralSequence,
                        member.Property.Locations.FirstOrDefault(),
                        member.Property.Name
                    )
                );
            }
        }

        // Declared-but-invalid keys on promoted nested models must fail the
        // root as well.
        var nestedSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach (
            var nestedDiagnostic in SparseKeyAnalyzer.CollectNestedKeyDiagnostics(
                members,
                config,
                cancellationToken
            )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key =
                nestedDiagnostic.DescriptorId
                + "|"
                + nestedDiagnostic.Argument1
                + "|"
                + nestedDiagnostic.Location?.SourceSpan.ToString();
            if (nestedSeen.Add(key))
            {
                diagnostics.Add(nestedDiagnostic);
            }
        }
    }

    private static void CollectSparseIgnoreDiagnostics(
        INamedTypeSymbol root,
        SparseGeneratorConfig config,
        ImmutableArray<SparseGeneratorDiagnostic>.Builder diagnostics,
        CancellationToken cancellationToken
    )
    {
        var pending = new Stack<INamedTypeSymbol>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            var isFragmentModel = SparseModelDiscovery.IsFragmentModel(
                current,
                config,
                cancellationToken
            );
            var constructor = isFragmentModel
                ? ModelConstructorBinding.AnalyzeRoot(current, config, cancellationToken)
                : ModelConstructorBinding.AnalyzeStructural(current, config, cancellationToken);
            var properties = SparseModelDiscovery.GetReadableProperties(
                current,
                config,
                cancellationToken,
                includeSparseIgnored: true
            );
            foreach (var property in properties)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SparseModelDiscovery.IsSparseIgnored(property, config))
                {
                    continue;
                }

                if (
                    RoslynSymbolCompat.IsRequired(property)
                    || property.SetMethod?.IsInitOnly == true
                    || constructor?.Parameters.Any(parameter =>
                        string.Equals(
                            parameter.PropertyName,
                            property.Name,
                            StringComparison.Ordinal
                        ) && !parameter.HasExplicitDefaultValue
                    ) == true
                )
                {
                    diagnostics.Add(
                        new SparseGeneratorDiagnostic(
                            config.EffectiveDiagnosticIds.SparseIgnoreUnsupportedProperty,
                            property.Locations.FirstOrDefault(),
                            property.Name
                        )
                    );
                }
            }

            foreach (
                var member in SparseModelDiscovery.GetMembers(current, config, cancellationToken)
            )
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (member.ChildModel is not null)
                {
                    pending.Push(member.ChildModel);
                }

                if (
                    member.Collection.ElementType is INamedTypeSymbol element
                    && (
                        SparseModelDiscovery.IsFragmentModel(element, config, cancellationToken)
                        || SparseModelDiscovery.IsStructuralType(element, config, cancellationToken)
                        || SparsePromotedDiscovery.IsPromotablePartial(
                            element,
                            config,
                            cancellationToken
                        )
                    )
                )
                {
                    pending.Push(element);
                }
            }
        }
    }

    public static IEnumerable<IPropertySymbol> UnsupportedStructuralMembers(
        INamedTypeSymbol model,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var pending = new Stack<INamedTypeSymbol>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        pending.Push(model);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current))
                continue;
            foreach (
                var property in SparseModelDiscovery
                    .GetMembers(current, config, cancellationToken)
                    .Select(static member => member.Property)
            )
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    property.Type
                        is not INamedTypeSymbol
                        {
                            TypeKind: TypeKind.Class,
                            SpecialType: SpecialType.None
                        } type
                    || SparseModelDiscovery.IsFrameworkType(type)
                    || SparseModelDiscovery.IsFragmentModel(type, config, cancellationToken)
                )
                    continue;
                if (
                    SparseCollectionAnalyzer.GetCollectionInfo(type).CloneKind
                    != SparseCloneCollectionKind.Unsupported
                )
                    continue;
                if (
                    SparseModelDiscovery.IsStructuralType(type, config, cancellationToken)
                    || SparsePromotedDiscovery.IsPromotablePartial(type, config, cancellationToken)
                )
                {
                    pending.Push(type);
                    continue;
                }
                var replace = property
                    .GetAttributes()
                    .Any(attribute =>
                        attribute.AttributeClass?.ToDisplayString()
                            == config.MergeAttributeMetadataName
                        && attribute.ConstructorArguments.FirstOrDefault().Value is int mode
                        && config.EffectiveMergeModeMap.Normalize(mode) == SparseMergeModes.Replace
                    );
                if (!replace)
                    yield return property;
            }
        }
    }
}
