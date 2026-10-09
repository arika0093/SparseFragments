using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace SparseFragments.Generator.Shared;

/// <summary>Per-compilation index backing comparison-rule inheritance.</summary>
/// <remarks>
/// <para>
/// Resolving inherited <c>SparseCompare</c> rules needs every fragment root
/// plus the set of models each root references. Recomputing that per model
/// scans the containing assembly once per generated model, which grows
/// quadratically in large consumer compilations.
/// </para>
/// <para>
/// The index is computed once per assembly symbol and shared by all models in
/// that compilation. New compilations produce new assembly symbols, so edited
/// trees invalidate the entry without explicit bookkeeping; entries are held
/// weakly and release with their compilation. Reference graphs mirror
/// <c>SparseComparisonRules</c> traversal exactly, so inheritance, ambiguity,
/// and veto behavior are unchanged.
/// </para>
/// </remarks>
internal sealed class SparseComparisonIndex
{
    private static readonly ConditionalWeakTable<
        IAssemblySymbol,
        Dictionary<string, SparseComparisonIndex>
    > Cache = new();

    private static readonly object Gate = new();

    private SparseComparisonIndex(
        ImmutableArray<INamedTypeSymbol> roots,
        ImmutableArray<HashSet<INamedTypeSymbol>> closures,
        bool hasComparisonRules
    )
    {
        Roots = roots;
        Closures = closures;
        HasComparisonRules = hasComparisonRules;
    }

    /// <summary>Fragment roots carrying the model attribute, in assembly order.</summary>
    public ImmutableArray<INamedTypeSymbol> Roots { get; }

    /// <summary>Whether any indexed root declares a comparison rule.</summary>
    /// <remarks>
    /// When false, inheritance cannot contribute rules and callers skip
    /// reference-graph work entirely.
    /// </remarks>
    public bool HasComparisonRules { get; }

    private ImmutableArray<HashSet<INamedTypeSymbol>> Closures { get; }

    /// <summary>Gets or builds the index for the model's containing assembly.</summary>
    public static SparseComparisonIndex ForAssembly(
        IAssemblySymbol assembly,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var key = config.ModelAttributeMetadataName + "\0" + config.ComparisonAttributeMetadataName;
        lock (Gate)
        {
            if (
                Cache.TryGetValue(assembly, out var entries)
                && entries.TryGetValue(key, out var hit)
            )
            {
                return hit;
            }

            var built = Build(assembly, config, cancellationToken);
            if (entries is null)
            {
                entries = new Dictionary<string, SparseComparisonIndex>(StringComparer.Ordinal);
                Cache.Add(assembly, entries);
            }

            entries[key] = built;
            return built;
        }
    }

    /// <summary>Whether the indexed root's member graph references the target model.</summary>
    public bool ReferencesModel(int rootIndex, INamedTypeSymbol target) =>
        Closures[rootIndex].Contains(target);

    private static SparseComparisonIndex Build(
        IAssemblySymbol assembly,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var roots = new List<INamedTypeSymbol>();
        var ruleFlags = new List<bool>();
        var hasRules = false;
        foreach (var type in GetTypes(assembly.GlobalNamespace, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!HasAttribute(type, config.ModelAttributeMetadataName))
            {
                continue;
            }

            var hasRule = HasComparisonAttribute(type, config.ComparisonAttributeMetadataName);
            roots.Add(type);
            ruleFlags.Add(hasRule);
            hasRules |= hasRule;
        }

        var closures = ImmutableArray<HashSet<INamedTypeSymbol>>.Empty;
        if (hasRules)
        {
            var builder = ImmutableArray.CreateBuilder<HashSet<INamedTypeSymbol>>(roots.Count);
            for (var index = 0; index < roots.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                builder.Add(CollectReferences(roots[index], cancellationToken));
            }

            closures = builder.MoveToImmutable();
        }

        return new SparseComparisonIndex(roots.ToImmutableArray(), closures, hasRules);
    }

    // Transitive reference closure mirroring SparseComparisonRules traversal:
    // type arguments are followed through framework types, while only
    // non-framework member graphs are expanded.
    private static HashSet<INamedTypeSymbol> CollectReferences(
        INamedTypeSymbol root,
        CancellationToken cancellationToken
    )
    {
        var collected = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        Collect(root, collected, visited, cancellationToken);
        return collected;
    }

    private static void Collect(
        ITypeSymbol type,
        HashSet<INamedTypeSymbol> collected,
        HashSet<INamedTypeSymbol> visited,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (type is IArrayTypeSymbol array)
        {
            Collect(array.ElementType, collected, visited, cancellationToken);
            return;
        }

        if (type is not INamedTypeSymbol named)
        {
            return;
        }

        foreach (var argument in named.TypeArguments)
        {
            Collect(argument, collected, visited, cancellationToken);
        }

        if (SparseModelDiscovery.IsFrameworkType(named) || !visited.Add(named))
        {
            return;
        }

        collected.Add(named);
        foreach (var property in named.GetMembers().OfType<IPropertySymbol>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                !property.IsStatic
                && !property.IsIndexer
                && property.DeclaredAccessibility == Accessibility.Public
                && property.GetMethod?.DeclaredAccessibility == Accessibility.Public
            )
            {
                Collect(property.Type, collected, visited, cancellationToken);
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetTypes(
        INamespaceSymbol root,
        CancellationToken cancellationToken
    )
    {
        var pending = new Stack<INamespaceOrTypeSymbol>(root.GetMembers());
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            if (current is INamespaceSymbol ns)
            {
                foreach (var member in ns.GetMembers())
                {
                    pending.Push(member);
                }
            }
            else if (current is INamedTypeSymbol type)
            {
                yield return type;
                foreach (var nested in type.GetTypeMembers())
                {
                    pending.Push(nested);
                }
            }
        }
    }

    private static bool HasAttribute(INamedTypeSymbol model, string attributeName) =>
        model
            .GetAttributes()
            .Any(attribute => MatchesAttribute(attribute.AttributeClass, attributeName));

    private static bool MatchesAttribute(INamedTypeSymbol? attributeType, string attributeName)
    {
        if (attributeType is null)
        {
            return false;
        }
        var simpleClass =
            attributeType.TypeKind == TypeKind.Class
            && attributeType.Arity == 0
            && attributeType.SpecialType == SpecialType.None;
        if (
            simpleClass
            && attributeType.NullableAnnotation != NullableAnnotation.Annotated
            && !attributeName.EndsWith(attributeType.Name, StringComparison.Ordinal)
        )
        {
            return false;
        }
        return attributeType.ToDisplayString() == attributeName;
    }

    private static bool HasComparisonAttribute(INamedTypeSymbol model, string? attributeName) =>
        attributeName is not null && HasAttribute(model, attributeName);
}
