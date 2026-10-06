using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace SparseFragments.Generator.Shared;

/// <summary>Collects validation diagnostics for discovered models and members.</summary>
internal static class SparseModelDiagnostics
{
    internal const int CustomMergeMode = 4;

    internal static void CollectMemberDiagnostics(
        INamedTypeSymbol model,
        ImmutableArray<SparseSymbolMemberModel> members,
        SparseGeneratorConfig config,
        ImmutableArray<SparseGeneratorDiagnostic>.Builder diagnostics,
        CancellationToken cancellationToken
    )
    {
        foreach (
            var member in ModelConstructionPlan.UnsupportedRequiredMembers(
                model,
                members.Select(static member => member.Property),
                cancellationToken
            )
        )
            diagnostics.Add(
                new SparseGeneratorDiagnostic(
                    SparseDiagnosticIds.UnsupportedRequired,
                    member.Locations.FirstOrDefault(),
                    member.Name
                )
            );

        foreach (var property in UnsupportedStructuralMembers(model, config, cancellationToken))
            diagnostics.Add(
                new SparseGeneratorDiagnostic(
                    SparseDiagnosticIds.UnsupportedStructural,
                    property.Locations.FirstOrDefault(),
                    property.Name
                )
            );

        // Members whose element declares key metadata but whose key is invalid fail
        // with the precise SPF012–SPF020 cause. Precompute them so downstream
        // artifact diagnostics (notably SPF008 from clone analysis) stay silent:
        // the invalid declaration itself is the actionable failure.
        var keyErrorProperties = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var member in members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                member.Collection.ElementType is INamedTypeSymbol element
                && SparseKeyAnalyzer.HasKeyDeclaration(element, config, cancellationToken)
                && !SparseKeyAnalyzer.CollectDiagnostics(element, config, cancellationToken).IsEmpty
            )
            {
                keyErrorProperties.Add(member.Property);
            }
        }

        foreach (
            var property in SparseCloneAnalysis.UnsupportedCloneMembers(
                model,
                config,
                cancellationToken
            )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (keyErrorProperties.Contains(property))
            {
                continue;
            }

            diagnostics.Add(
                new SparseGeneratorDiagnostic(
                    SparseDiagnosticIds.UnsupportedClone,
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
                    || !IsValidMergeStrategy(
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
                        SparseDiagnosticIds.InvalidMergeStrategy,
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
                        SparseDiagnosticIds.UnsupportedMerge,
                        member.Property.Locations.FirstOrDefault(),
                        member.Property.Name
                    )
                );
            }

            // Key validation runs for every member whose element declares key metadata,
            // even when keyed semantics are not required (scalar fallback or an explicit
            // whole-collection merge mode): an invalid declaration must fail with its
            // own SPF012–SPF020 cause rather than being silently ignored. Members with
            // no declaration keep the SPF011 unkeyed-sequence check below.
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
                        SparseDiagnosticIds.UnkeyedStructuralSequence,
                        member.Property.Locations.FirstOrDefault(),
                        member.Property.Name
                    )
                );
            }
        }

        // Declared-but-invalid keys on promoted nested models must fail the root as
        // well: nested keyed semantics are generated from the same metadata.
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
                        && mode == 0
                    );
                if (!replace)
                    yield return property;
            }
        }
    }

    private static bool IsValidMergeStrategy(
        INamedTypeSymbol strategyType,
        ITypeSymbol memberType,
        bool isNestedModel,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        if (
            isNestedModel
            || strategyType.TypeKind != TypeKind.Class
            || strategyType.IsAbstract
            || strategyType.Arity != 0
        )
        {
            return false;
        }

        for (var current = strategyType; current is not null; current = current.ContainingType)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                current.DeclaredAccessibility
                is not (Accessibility.Public or Accessibility.Internal)
            )
            {
                return false;
            }
        }

        var hasConstructor = strategyType.InstanceConstructors.Any(static constructor =>
            constructor.Parameters.Length == 0
            && constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal
        );
        if (!hasConstructor)
        {
            return false;
        }

        for (var current = strategyType; current is not null; current = current.BaseType)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                current.OriginalDefinition.ToDisplayString() == config.MergeStrategyBaseMetadataName
                && SymbolEqualityComparer.Default.Equals(current.TypeArguments[0], memberType)
            )
            {
                return true;
            }
        }

        return false;
    }
}
