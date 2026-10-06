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
/// Intentionally narrow (issue #280): first-class configuration-model shapes are
/// arrays, lists, sets and dictionaries. Stateful or specialized BCL containers
/// (queues, stacks, concurrent collections, <c>BlockingCollection</c>,
/// <c>PriorityQueue</c>, <c>LinkedList</c>, sorted/observable/read-only wrappers
/// and immutable collections) are <see cref="Unsupported"/> and require an
/// explicit custom clone/merge policy.
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
/// <remarks>
/// Orthogonal to <see cref="SparseCollectionKind"/> (merge shape) and
/// <see cref="SparseCloneCollectionKind"/> (clone shape): describes whether a
/// sequence carries scalar values, structurally keyed elements, or dictionary
/// entries. Existing emitters ignore this and keep current behavior; keyed
/// emitters (issue #3, later part) will consume it.
/// </remarks>
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
            // Narrowed (issue #280): Queue/Stack, concurrent collections,
            // BlockingCollection, PriorityQueue, LinkedList, SortedSet,
            // ObservableCollection/ReadOnlyCollection and immutable collections
            // are intentionally unsupported. Use an array, List, HashSet or
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

    /// <summary>Metadata name of the key attribute declared by the runtime.</summary>
    /// <remarks>
    /// The attribute type itself ships with the runtime assembly (issue #3); the
    /// analyzer resolves it by metadata name so analysis degrades to "no key"
    /// when the attribute is absent.
    /// </remarks>
    public const string KeyAttributeMetadataName = "SparseFragments.SparseKeyAttribute";

    /// <summary>Determines whether an element type needs keyed granular semantics.</summary>
    /// <remarks>
    /// Keyed semantics apply only to fragment/promotable elements (types with
    /// generated Fragment/Patch). Plain POCO sequences remain atomic scalar
    /// sequences to preserve backward compatibility; only fragment element
    /// sequences without a usable key report SPF011.
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
    /// <remarks>
    /// Key source precedence: a model-level <c>[SparseKey("A", "B")]</c> on the
    /// element type wins when present (property-level attributes are then ignored);
    /// otherwise property-level <c>[SparseKey]</c> members apply, ordered by
    /// <c>Order</c> then property name. Composite keys preserve model-level
    /// declaration order. A single key reports its property type; a composite key
    /// reports a <c>ValueTuple</c> of the property types in key order. Only usable
    /// (scalar, non-collection) key properties count; unknown or unusable names are
    /// dropped, and an empty result means "no usable key".
    /// </remarks>
    public static bool TryDiscoverKeys(
        INamedTypeSymbol element,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken,
        out ImmutableArray<string> keyPropertyNames,
        out string? keyTypeName
    )
    {
        keyPropertyNames = ImmutableArray<string>.Empty;
        keyTypeName = null;

        var properties = new Dictionary<string, IPropertySymbol>(StringComparer.Ordinal);
        foreach (
            var property in SparseModelDiscovery.GetReadableProperties(element, cancellationToken)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            properties[property.Name] = property;
        }

        if (properties.Count == 0)
        {
            return false;
        }

        foreach (var attribute in element.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attribute.AttributeClass?.ToDisplayString() != KeyAttributeMetadataName)
            {
                continue;
            }

            var declared = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var argument in attribute.ConstructorArguments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (argument.Kind == TypedConstantKind.Array)
                {
                    foreach (var value in argument.Values)
                    {
                        if (value.Value is string name && name.Length > 0 && seen.Add(name))
                        {
                            declared.Add(name);
                        }
                    }
                }
                else if (argument.Value is string name && name.Length > 0 && seen.Add(name))
                {
                    declared.Add(name);
                }
            }

            if (
                TryBuildKey(
                    properties,
                    declared,
                    config,
                    cancellationToken,
                    out keyPropertyNames,
                    out keyTypeName
                )
            )
            {
                return true;
            }

            // Model-level present but unusable (unknown/non-scalar names): fall through
            // to property-level rather than suppressing valid property keys silently.
            break;
        }

        var keyed = new List<(int Order, string Name)>();
        foreach (var property in properties.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryGetKeyOrder(property, cancellationToken, out var order))
            {
                keyed.Add((order, property.Name));
            }
        }

        keyed.Sort(
            static (left, right) =>
                left.Order != right.Order
                    ? left.Order.CompareTo(right.Order)
                    : string.Compare(left.Name, right.Name, StringComparison.Ordinal)
        );

        var ordered = new List<string>(keyed.Count);
        foreach (var (order, name) in keyed)
        {
            _ = order;
            ordered.Add(name);
        }

        return TryBuildKey(
            properties,
            ordered,
            config,
            cancellationToken,
            out keyPropertyNames,
            out keyTypeName
        );
    }

    /// <summary>Determines whether a member requires the SPF011 unkeyed-sequence diagnostic.</summary>
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
        // MergeMode integers mirror SparseFragments.MergeMode: 0 Replace, 1 Deep,
        // 2 Append, 3 SetUnion, 4 Custom.
        if (
            member.MergeMode is 2 or 3
            || member.MergeMode == SparseModelDiagnostics.CustomMergeMode
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

    private static bool TryGetKeyOrder(
        IPropertySymbol property,
        CancellationToken cancellationToken,
        out int order
    )
    {
        order = 0;
        foreach (var attribute in property.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attribute.AttributeClass?.ToDisplayString() != KeyAttributeMetadataName)
            {
                continue;
            }

            foreach (var named in attribute.NamedArguments)
            {
                if (named.Key == "Order" && named.Value.Value is int namedOrder)
                {
                    order = namedOrder;
                    return true;
                }
            }

            if (attribute.ConstructorArguments.FirstOrDefault().Value is int constructorOrder)
            {
                order = constructorOrder;
            }

            return true;
        }

        return false;
    }

    private static bool TryBuildKey(
        Dictionary<string, IPropertySymbol> properties,
        IEnumerable<string> names,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken,
        out ImmutableArray<string> keyPropertyNames,
        out string? keyTypeName
    )
    {
        var resolved = new List<IPropertySymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seen.Add(name))
            {
                continue;
            }

            if (
                properties.TryGetValue(name, out var property)
                && IsUsableKeyType(property.Type, config, cancellationToken)
            )
            {
                resolved.Add(property);
            }
        }

        if (resolved.Count == 0)
        {
            keyPropertyNames = ImmutableArray<string>.Empty;
            keyTypeName = null;
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<string>(resolved.Count);
        foreach (var property in resolved)
        {
            builder.Add(property.Name);
        }

        keyPropertyNames = builder.ToImmutable();
        keyTypeName =
            resolved.Count == 1
                ? NonNullableTypeName(resolved[0].Type)
                : "("
                    + string.Join(
                        ", ",
                        resolved.Select(static property => NonNullableTypeName(property.Type))
                    )
                    + ")";
        return true;
    }

    private static bool IsUsableKeyType(
        ITypeSymbol type,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (type is IArrayTypeSymbol)
        {
            return false;
        }

        if (
            type is INamedTypeSymbol
            && GetCollectionInfo(type).CloneKind != SparseCloneCollectionKind.Unsupported
        )
        {
            return false;
        }

        return SparseModelDiscovery.ClassifyStructuralType(type, config, cancellationToken)
            == SparseModelDiscovery.StructuralTypeKind.Scalar;
    }

    private static string NonNullableTypeName(ITypeSymbol type) =>
        type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
