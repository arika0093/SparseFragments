using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace SparseFragments.Generator.Shared;

/// <summary>Merge-relevant collection category used by generated fragments.</summary>
internal enum SparseCollectionKind
{
    Unsupported,
    Array,
    List,
    Set,
}

/// <summary>Clone-relevant collection category used by generated fragments.</summary>
/// <remarks>
/// Intentionally narrow: first-class shapes are arrays, lists, sets and dictionaries.
/// Other BCL containers are <see cref="Unsupported"/> and require an explicit custom clone/merge policy.
/// </remarks>
internal enum SparseCloneCollectionKind
{
    Unsupported,
    Array,
    List,
    Set,
    Dictionary,
}

/// <summary>Granular-merge semantic of a collection member.</summary>
/// <remarks>Orthogonal to merge/clone shape: whether a sequence carries scalar values, keyed elements, or dictionary entries.</remarks>
internal enum SparseCollectionSemantic
{
    None,
    ScalarSequence,
    KeyedSequence,
    Dictionary,
}

/// <summary>Describes a discovered collection type.</summary>
internal sealed class SparseSymbolCollectionInfo(
    SparseCollectionKind kind,
    SparseCloneCollectionKind cloneKind,
    ITypeSymbol elementType,
    ITypeSymbol? valueType,
    INamedTypeSymbol? namedType
)
{
    public SparseCollectionKind Kind { get; } = kind;
    public SparseCloneCollectionKind CloneKind { get; } = cloneKind;
    public ITypeSymbol ElementType { get; } = elementType;
    public ITypeSymbol? ValueType { get; } = valueType;
    public INamedTypeSymbol? NamedType { get; } = namedType;
    public static SparseSymbolCollectionInfo Unsupported { get; } =
        new(
            SparseCollectionKind.Unsupported,
            SparseCloneCollectionKind.Unsupported,
            null!,
            null,
            null
        );
}

/// <summary>Discovers merge and clone semantics for candidate member types.</summary>
internal static class SparseCollectionAnalyzer
{
    public static bool HashSetSupportsCapacity(Compilation compilation) =>
        compilation
            .GetTypeByMetadataName("System.Collections.Generic.HashSet`1")
            ?.InstanceConstructors.Any(static constructor =>
                constructor.DeclaredAccessibility == Accessibility.Public
                && constructor.Parameters.Length == 2
                && constructor.Parameters[0].Type.SpecialType == SpecialType.System_Int32
                && constructor.Parameters[1].Type is INamedTypeSymbol comparer
                && comparer.OriginalDefinition.ToDisplayString()
                    == "System.Collections.Generic.IEqualityComparer<T>"
            ) == true;

    public static bool HashSetImplementsReadOnlySet(Compilation compilation)
    {
        var readOnlySet = compilation.GetTypeByMetadataName(
            "System.Collections.Generic.IReadOnlySet`1"
        );
        if (readOnlySet is null)
            return false;
        var hashSet = compilation.GetTypeByMetadataName("System.Collections.Generic.HashSet`1");
        if (hashSet is null)
            return false;
        var constructed = hashSet.Construct(compilation.GetSpecialType(SpecialType.System_Int32));
        return constructed.AllInterfaces.Any(implemented =>
            SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, readOnlySet)
        );
    }

    public static SparseSymbolCollectionInfo GetCollectionInfo(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            return new SparseSymbolCollectionInfo(
                SparseCollectionKind.Array,
                SparseCloneCollectionKind.Array,
                array.ElementType,
                null,
                null
            );
        }

        if (type is not INamedTypeSymbol named || named.TypeArguments.Length is < 1 or > 2)
        {
            return SparseSymbolCollectionInfo.Unsupported;
        }

        var elementType = named.TypeArguments[0];
        var definition = named.ConstructedFrom.ToDisplayString();
        var kind = definition switch
        {
            "System.Collections.Generic.List<T>" => SparseCollectionKind.List,
            "System.Collections.Generic.IList<T>" => SparseCollectionKind.List,
            "System.Collections.Generic.IEnumerable<T>"
            or "System.Collections.Generic.IReadOnlyCollection<T>"
            or "System.Collections.Generic.IReadOnlyList<T>" => SparseCollectionKind.Array,
            "System.Collections.Generic.HashSet<T>"
            or "System.Collections.Generic.ISet<T>"
            or "System.Collections.Generic.IReadOnlySet<T>" => SparseCollectionKind.Set,
            _ => SparseCollectionKind.Unsupported,
        };

        var cloneKind = definition switch
        {
            "System.Collections.Generic.List<T>" or "System.Collections.Generic.IList<T>" =>
                SparseCloneCollectionKind.List,
            "System.Collections.Generic.IEnumerable<T>"
            or "System.Collections.Generic.IReadOnlyCollection<T>"
            or "System.Collections.Generic.IReadOnlyList<T>" => SparseCloneCollectionKind.Array,
            "System.Collections.Generic.HashSet<T>"
            or "System.Collections.Generic.ISet<T>"
            or "System.Collections.Generic.IReadOnlySet<T>" => SparseCloneCollectionKind.Set,
            "System.Collections.Generic.Dictionary<TKey, TValue>"
            or "System.Collections.Generic.SortedDictionary<TKey, TValue>"
            or "System.Collections.Generic.SortedList<TKey, TValue>"
            or "System.Collections.Generic.IDictionary<TKey, TValue>"
            or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>" =>
                SparseCloneCollectionKind.Dictionary,
            // Other BCL shapes are intentionally unsupported; use an array, List, HashSet or
            // Dictionary shape, or provide a custom clone/merge policy.
            _ => SparseCloneCollectionKind.Unsupported,
        };

        return new SparseSymbolCollectionInfo(
            kind,
            cloneKind,
            elementType,
            named.TypeArguments.Length == 2 ? named.TypeArguments[1] : null,
            named
        );
    }

    /// <summary>Determines whether an element type needs keyed granular semantics.</summary>
    /// <remarks>
    /// Keyed semantics apply only to fragment/promotable elements (types with
    /// generated Fragment/Patch). Plain POCO sequences remain atomic scalar
    /// sequences to preserve backward compatibility; only fragment element
    /// sequences without a usable key report the configured unkeyed-sequence diagnostic.
    /// </remarks>
    public static bool IsStructuralElement(
        ITypeSymbol element,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (
            element is INamedTypeSymbol named
            && (
                SparseModelDiscovery.IsFragmentModel(named, config, cancellationToken)
                || SparsePromotedDiscovery.IsPromotablePartial(named, config, cancellationToken)
            )
        )
        {
            return true;
        }

        return false;
    }

    /// <summary>Classifies the granular-merge semantic of a discovered collection.</summary>
    /// <remarks>
    /// Dictionaries are keyed by <c>TKey</c> inherently. Only ordered sequences
    /// (<c>Array</c>/<c>List</c> kinds) with structural elements are keyed-sequence
    /// candidates; sets keep set semantics (<see cref="SparseCollectionSemantic.None"/>)
    /// and scalar element sequences need no key.
    /// </remarks>
    public static SparseCollectionSemantic ClassifySemantic(
        SparseSymbolCollectionInfo collection,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (collection.CloneKind == SparseCloneCollectionKind.Dictionary)
        {
            return SparseCollectionSemantic.Dictionary;
        }

        if (
            collection.Kind is not (SparseCollectionKind.Array or SparseCollectionKind.List)
            || collection.CloneKind == SparseCloneCollectionKind.Unsupported
            || collection.ElementType is null
        )
        {
            return SparseCollectionSemantic.None;
        }

        return IsStructuralElement(collection.ElementType, config, cancellationToken)
            ? SparseCollectionSemantic.KeyedSequence
            : SparseCollectionSemantic.ScalarSequence;
    }

    /// <summary>Discovers the structural key of an element type.</summary>
    /// <remarks>Exactly one key mechanism may apply; conflicts yield no key and diagnostics.</remarks>
    public static bool TryDiscoverKeys(
        INamedTypeSymbol element,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken,
        out ImmutableArray<string> keyPropertyNames,
        out string? keyTypeName
    )
    {
        if (
            SparseKeyAnalyzer.TryGetKeyInfo(element, config, cancellationToken, out var info)
            && info is not null
        )
        {
            keyPropertyNames = info.PropertyNames;
            keyTypeName = info.KeyTypeName;
            return true;
        }

        keyPropertyNames = ImmutableArray<string>.Empty;
        keyTypeName = null;
        return false;
    }

    /// <summary>Discovers full key metadata (including the defining mechanism).</summary>
    public static bool TryDiscoverKeyInfo(
        INamedTypeSymbol element,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken,
        out SparseKeyInfo? info
    ) => SparseKeyAnalyzer.TryGetKeyInfo(element, config, cancellationToken, out info);

    /// <summary>Determines whether a member requires the configured unkeyed-sequence diagnostic.</summary>
    /// <remarks>
    /// Escape hatch: explicit <c>Append</c>/<c>SetUnion</c>/<c>Custom</c> merge modes
    /// keep legacy whole-collection semantics without a key. <c>Replace</c> (whether
    /// default or explicit) and <c>Deep</c> do not escape: an unkeyed structural
    /// sequence must not silently fall back to granular semantics.
    /// </remarks>
    public static bool IsUnkeyedStructuralSequence(
        SparseSymbolMemberModel member,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (
            member.MergeMode is SparseMergeModes.Append or SparseMergeModes.SetUnion
            || member.MergeMode == SparseMergeModes.Custom
        )
        {
            return false;
        }

        if (
            ClassifySemantic(member.Collection, config, cancellationToken)
            != SparseCollectionSemantic.KeyedSequence
        )
        {
            return false;
        }

        return member.Collection.ElementType is not INamedTypeSymbol namedElement
            || !TryDiscoverKeys(namedElement, config, cancellationToken, out _, out _);
    }
}
