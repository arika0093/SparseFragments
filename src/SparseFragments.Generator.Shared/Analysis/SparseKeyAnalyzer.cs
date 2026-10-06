using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace SparseFragments.Generator.Shared;

/// <summary>Key-definition mechanism that produced a discovered key.</summary>
internal enum SparseKeyKind
{
    None,

    /// <summary>One property marked with parameterless <c>[SparseKey]</c>.</summary>
    Property,

    /// <summary>Type-level <c>[SparseKey(nameof(...), ...)]</c> composite declaration.</summary>
    Composite,

    /// <summary><c>ISparseKeyed&lt;TKey&gt;</c> implementation.</summary>
    Interface,
}

/// <summary>Validated key metadata for a structural element type.</summary>
/// <remarks>
/// <para>
/// For <see cref="SparseKeyKind.Property"/> and <see cref="SparseKeyKind.Composite"/>,
/// <see cref="PropertyNames"/> holds the key properties in key order (significant for
/// composites) and <see cref="KeyTypeName"/> is either the single property type or a
/// <c>ValueTuple</c> of the component types in key order. The tuple representation is
/// strongly typed and collision-safe by construction (no generated identifiers), with
/// component-wise equality via <c>EqualityComparer&lt;T&gt;.Default</c>.
/// </para>
/// <para>
/// For <see cref="SparseKeyKind.Interface"/>, <see cref="PropertyNames"/> is empty and
/// <see cref="KeyTypeName"/> is the <c>TKey</c> type argument; extraction is
/// <c>element.SparseKey</c>.
/// </para>
/// </remarks>
internal sealed record SparseKeyInfo(
    SparseKeyKind Kind,
    ImmutableArray<string> PropertyNames,
    string KeyTypeName
);

/// <summary>Discovers and validates SparseFragments key metadata on element types.</summary>
/// <remarks>
/// Exactly one key-definition mechanism may apply to a structural type: one
/// property-level <c>[SparseKey]</c>, one type-level composite declaration, or one
/// <c>ISparseKeyed&lt;TKey&gt;</c> implementation. There is no precedence between
/// conflicting mechanisms; conflicts are errors. Validation is strict: any invalid
/// shape yields diagnostics and no key (callers must fail generation for the affected
/// model rather than silently selecting another key source).
/// </remarks>
internal static class SparseKeyAnalyzer
{
    /// <summary>Metadata name of the computed-identity escape hatch.</summary>
    public const string KeyedInterfaceMetadataName = "SparseFragments.ISparseKeyed<TKey>";

    /// <summary>Name of the interface key property.</summary>
    public const string SparseKeyPropertyName = "SparseKey";

    private const string KeyAttributeMetadataName = "SparseFragments.SparseKeyAttribute";

    /// <summary>Determines whether an element type declares any key metadata.</summary>
    public static bool HasKeyDeclaration(
        INamedTypeSymbol element,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return CollectMechanisms(element, config, cancellationToken).KindCount > 0;
    }

    /// <summary>Strictly discovers the usable key of an element type, if any.</summary>
    /// <remarks>
    /// Returns <c>false</c> when no mechanism applies or when any applicable mechanism
    /// is invalid (conflicts, bad shapes, unusable key types). Use
    /// <see cref="CollectDiagnostics"/> to report the corresponding diagnostics.
    /// </remarks>
    public static bool TryGetKeyInfo(
        INamedTypeSymbol element,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken,
        out SparseKeyInfo? info
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        info = null;
        var mechanisms = CollectMechanisms(element, config, cancellationToken);
        if (mechanisms.KindCount != 1)
        {
            return false;
        }

        if (mechanisms.PropertyMarks.Count > 1)
        {
            return false;
        }

        return TryBuildValidInfo(element, mechanisms, config, cancellationToken, out info);
    }

    /// <summary>Collects key-shape diagnostics for an element type.</summary>
    /// <remarks>
    /// Returns an empty array when the element has no key declaration at all (callers
    /// then report SPF011 for structural sequences that require keyed semantics) or
    /// when its single mechanism is fully valid. Any conflict or invalid shape yields
    /// one or more SPF012–SPF020 diagnostics and no key.
    /// </remarks>
    public static ImmutableArray<SparseGeneratorDiagnostic> CollectDiagnostics(
        INamedTypeSymbol element,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mechanisms = CollectMechanisms(element, config, cancellationToken);
        if (mechanisms.KindCount == 0)
        {
            return ImmutableArray<SparseGeneratorDiagnostic>.Empty;
        }

        var diagnostics = ImmutableArray.CreateBuilder<SparseGeneratorDiagnostic>();
        if (mechanisms.KindCount > 1)
        {
            diagnostics.Add(
                new SparseGeneratorDiagnostic(
                    SparseDiagnosticIds.ConflictingKeyMechanisms,
                    element.Locations.FirstOrDefault(),
                    element.Name
                )
            );
        }

        if (mechanisms.PropertyMarks.Count > 1)
        {
            diagnostics.Add(
                new SparseGeneratorDiagnostic(
                    SparseDiagnosticIds.MultiplePropertyKeys,
                    element.Locations.FirstOrDefault(),
                    element.Name
                )
            );
        }

        CollectMechanismDiagnostics(element, mechanisms, config, diagnostics, cancellationToken);
        return diagnostics.ToImmutable();
    }

    /// <summary>
    /// Validates keys for keyed-sequence candidates reachable from root members (root
    /// members plus transitively promoted nested models) and returns diagnostics that
    /// must fail generation of the analyzed root.
    /// </summary>
    /// <remarks>
    /// SPF011 itself is still owned by <see cref="SparseModelDiagnostics"/> for root
    /// members; this helper only reports SPF012–SPF020 shape errors so callers can
    /// prefer specific key diagnostics over the generic unkeyed-sequence error.
    /// </remarks>
    public static ImmutableArray<SparseGeneratorDiagnostic> CollectNestedKeyDiagnostics(
        ImmutableArray<SparseSymbolMemberModel> rootMembers,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var diagnostics = ImmutableArray.CreateBuilder<SparseGeneratorDiagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (
            var promoted in SparsePromotedDiscovery.CollectPromotedTypes(
                rootMembers,
                config,
                cancellationToken
            )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (
                var member in SparseModelDiscovery.GetMembers(promoted, config, cancellationToken)
            )
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddMemberKeyDiagnostics(member, config, seen, diagnostics, cancellationToken);
            }
        }

        return diagnostics.ToImmutable();
    }

    internal static void AddMemberKeyDiagnostics(
        SparseSymbolMemberModel member,
        SparseGeneratorConfig config,
        HashSet<string> seen,
        ImmutableArray<SparseGeneratorDiagnostic>.Builder diagnostics,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!RequiresKeyedSemantics(member, config, cancellationToken))
        {
            return;
        }

        if (member.Collection.ElementType is not INamedTypeSymbol element)
        {
            return;
        }

        foreach (var diagnostic in CollectDiagnostics(element, config, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key =
                diagnostic.DescriptorId
                + "|"
                + diagnostic.Argument1
                + "|"
                + diagnostic.Location?.SourceSpan.ToString();
            if (seen.Add(key))
            {
                diagnostics.Add(diagnostic);
            }
        }
    }

    /// <summary>Determines whether a member is a keyed-sequence candidate needing a key.</summary>
    /// <remarks>
    /// Mirrors the escape-hatch rule of
    /// <see cref="SparseCollectionAnalyzer.IsUnkeyedStructuralSequence"/>: explicit
    /// <c>Append</c>/<c>SetUnion</c>/<c>Custom</c> merge modes keep legacy
    /// whole-collection semantics without a key.
    /// </remarks>
    public static bool RequiresKeyedSemantics(
        SparseSymbolMemberModel member,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (
            member.MergeMode is 2 or 3
            || member.MergeMode == SparseModelDiagnostics.CustomMergeMode
        )
        {
            return false;
        }

        return SparseCollectionAnalyzer.ClassifySemantic(
                member.Collection,
                config,
                cancellationToken
            ) == SparseCollectionSemantic.KeyedSequence;
    }

    private sealed class KeyMechanisms
    {
        public List<IPropertySymbol> PropertyMarks { get; } = new();
        public List<AttributeData> PropertyMarkAttributes { get; } = new();
        public List<AttributeData> TypeAttributes { get; } = new();
        public List<INamedTypeSymbol> KeyedInterfaces { get; } = new();

        public int KindCount =>
            (PropertyMarks.Count > 0 ? 1 : 0)
            + (TypeAttributes.Count > 0 ? 1 : 0)
            + (KeyedInterfaces.Count > 0 ? 1 : 0);
    }

    private static KeyMechanisms CollectMechanisms(
        INamedTypeSymbol element,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        _ = config;
        var mechanisms = new KeyMechanisms();

        foreach (var property in GetAllProperties(element, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var attribute in property.GetAttributes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (attribute.AttributeClass?.ToDisplayString() == KeyAttributeMetadataName)
                {
                    mechanisms.PropertyMarks.Add(property);
                    mechanisms.PropertyMarkAttributes.Add(attribute);
                    break;
                }
            }
        }

        foreach (var attribute in element.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attribute.AttributeClass?.ToDisplayString() == KeyAttributeMetadataName)
            {
                mechanisms.TypeAttributes.Add(attribute);
            }
        }

        foreach (var implemented in element.AllInterfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                implemented.OriginalDefinition?.ToDisplayString() == KeyedInterfaceMetadataName
                && implemented.TypeArguments.Length == 1
            )
            {
                mechanisms.KeyedInterfaces.Add(implemented);
            }
        }

        return mechanisms;
    }

    private static IEnumerable<IPropertySymbol> GetAllProperties(
        INamedTypeSymbol model,
        CancellationToken cancellationToken
    )
    {
        var hierarchy = new Stack<INamedTypeSymbol>();
        for (
            var current = model;
            current is not null && current.SpecialType != SpecialType.System_Object;
            current = current.BaseType
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            hierarchy.Push(current);
        }

        // Most-derived declaration wins per name (base-first overwrite order).
        var properties = new Dictionary<string, IPropertySymbol>(StringComparer.Ordinal);
        while (hierarchy.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var property in hierarchy.Pop().GetMembers().OfType<IPropertySymbol>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                properties[property.Name] = property;
            }
        }

        return properties.Values;
    }

    private static bool TryBuildValidInfo(
        INamedTypeSymbol element,
        KeyMechanisms mechanisms,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken,
        out SparseKeyInfo? info
    )
    {
        info = null;
        if (mechanisms.PropertyMarks.Count == 1 && mechanisms.KindCount == 1)
        {
            var property = mechanisms.PropertyMarks[0];
            var attribute = mechanisms.PropertyMarkAttributes[0];
            if (HasAttributeArguments(attribute))
            {
                return false;
            }

            if (!IsPubliclyReadableInstance(property))
            {
                return false;
            }

            if (GetKeyTypeProblem(property.Type, config, cancellationToken) is not null)
            {
                return false;
            }

            info = new SparseKeyInfo(
                SparseKeyKind.Property,
                ImmutableArray.Create(property.Name),
                NonNullableTypeName(property.Type)
            );
            return true;
        }

        if (mechanisms.TypeAttributes.Count > 0 && mechanisms.KindCount == 1)
        {
            return TryBuildCompositeInfo(element, mechanisms, config, cancellationToken, out info);
        }

        if (mechanisms.KeyedInterfaces.Count > 0 && mechanisms.KindCount == 1)
        {
            return TryBuildInterfaceInfo(element, mechanisms, config, cancellationToken, out info);
        }

        return false;
    }

    private static bool TryBuildCompositeInfo(
        INamedTypeSymbol element,
        KeyMechanisms mechanisms,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken,
        out SparseKeyInfo? info
    )
    {
        info = null;
        // Multiple type-level attributes: only the first is meaningful; extras are
        // reported by CollectMechanismDiagnostics. Discovery stays strict.
        if (mechanisms.TypeAttributes.Count != 1)
        {
            return false;
        }

        if (!TryExtractComponentNames(mechanisms.TypeAttributes[0], out var names))
        {
            return false;
        }

        var readable = GetReadablePropertiesByName(element, cancellationToken);
        var resolved = new List<IPropertySymbol>(names.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seen.Add(name))
            {
                return false;
            }

            if (
                !readable.TryGetValue(name, out var property)
                || GetKeyTypeProblem(property.Type, config, cancellationToken) is not null
            )
            {
                return false;
            }

            resolved.Add(property);
        }

        if (resolved.Count == 0)
        {
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<string>(resolved.Count);
        foreach (var property in resolved)
        {
            builder.Add(property.Name);
        }

        info = new SparseKeyInfo(
            SparseKeyKind.Composite,
            builder.ToImmutable(),
            resolved.Count == 1
                ? NonNullableTypeName(resolved[0].Type)
                : "("
                    + string.Join(
                        ", ",
                        resolved.Select(static property => NonNullableTypeName(property.Type))
                    )
                    + ")"
        );
        return true;
    }

    private static bool TryBuildInterfaceInfo(
        INamedTypeSymbol element,
        KeyMechanisms mechanisms,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken,
        out SparseKeyInfo? info
    )
    {
        info = null;
        var distinctKeys = mechanisms
            .KeyedInterfaces.Select(static match => match.TypeArguments[0])
            .Distinct(
                (System.Collections.Generic.IEqualityComparer<Microsoft.CodeAnalysis.ITypeSymbol>)
                    SymbolEqualityComparer.Default
            )
            .ToArray();
        if (distinctKeys.Length != 1)
        {
            return false;
        }

        var keyType = distinctKeys[0];
        if (keyType is IErrorTypeSymbol)
        {
            return false;
        }

        if (GetKeyTypeProblem(keyType, config, cancellationToken) is not null)
        {
            return false;
        }

        if (!HasPublicReadableSparseKey(element, cancellationToken))
        {
            return false;
        }

        info = new SparseKeyInfo(
            SparseKeyKind.Interface,
            ImmutableArray<string>.Empty,
            NonNullableTypeName(keyType)
        );
        return true;
    }

    private static bool HasPublicReadableSparseKey(
        INamedTypeSymbol element,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (
            var property in element.GetMembers(SparseKeyPropertyName).OfType<IPropertySymbol>()
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                IsPubliclyReadableInstance(property)
                && property.ExplicitInterfaceImplementations.IsEmpty
            )
            {
                return true;
            }
        }

        return false;
    }

    private static void CollectMechanismDiagnostics(
        INamedTypeSymbol element,
        KeyMechanisms mechanisms,
        SparseGeneratorConfig config,
        ImmutableArray<SparseGeneratorDiagnostic>.Builder diagnostics,
        CancellationToken cancellationToken
    )
    {
        // Property-level marks.
        for (var index = 0; index < mechanisms.PropertyMarks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var property = mechanisms.PropertyMarks[index];
            var attribute = mechanisms.PropertyMarkAttributes[index];
            if (HasAttributeArguments(attribute))
            {
                diagnostics.Add(
                    new SparseGeneratorDiagnostic(
                        SparseDiagnosticIds.InvalidKeyAttributeShape,
                        property.Locations.FirstOrDefault(),
                        element.Name + "." + property.Name
                    )
                );
                continue;
            }

            // Only validate the property itself when it is the single mechanism;
            // conflicts (SPF012) and multiples (SPF013) already explain the failure.
            if (mechanisms.KindCount != 1 || mechanisms.PropertyMarks.Count != 1)
            {
                continue;
            }

            if (!IsPubliclyReadableInstance(property))
            {
                diagnostics.Add(
                    new SparseGeneratorDiagnostic(
                        SparseDiagnosticIds.InaccessibleKeyProperty,
                        property.Locations.FirstOrDefault(),
                        element.Name + "." + property.Name
                    )
                );
                continue;
            }

            AddKeyTypeDiagnostic(
                element,
                property,
                property.Type,
                config,
                diagnostics,
                cancellationToken
            );
        }

        // Type-level declarations.
        if (mechanisms.TypeAttributes.Count > 1)
        {
            foreach (var extra in mechanisms.TypeAttributes.Skip(1))
            {
                cancellationToken.ThrowIfCancellationRequested();
                diagnostics.Add(
                    new SparseGeneratorDiagnostic(
                        SparseDiagnosticIds.ConflictingKeyMechanisms,
                        AttributeLocation(extra, element),
                        element.Name
                    )
                );
            }
        }

        if (mechanisms.TypeAttributes.Count == 1 && mechanisms.KindCount == 1)
        {
            CollectCompositeDiagnostics(
                element,
                mechanisms.TypeAttributes[0],
                config,
                diagnostics,
                cancellationToken
            );
        }
        else
        {
            foreach (var attribute in mechanisms.TypeAttributes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryExtractComponentNames(attribute, out _))
                {
                    diagnostics.Add(
                        new SparseGeneratorDiagnostic(
                            SparseDiagnosticIds.InvalidKeyAttributeShape,
                            AttributeLocation(attribute, element),
                            element.Name
                        )
                    );
                }
            }
        }

        // Interface implementations.
        if (mechanisms.KeyedInterfaces.Count > 0 && mechanisms.KindCount == 1)
        {
            CollectInterfaceDiagnostics(
                element,
                mechanisms,
                config,
                diagnostics,
                cancellationToken
            );
        }
        else if (mechanisms.KeyedInterfaces.Count > 0)
        {
            var distinctKeys = mechanisms
                .KeyedInterfaces.Select(static match => match.TypeArguments[0])
                .Distinct(
                    (System.Collections.Generic.IEqualityComparer<Microsoft.CodeAnalysis.ITypeSymbol>)
                        SymbolEqualityComparer.Default
                )
                .ToArray();
            if (distinctKeys.Length > 1)
            {
                diagnostics.Add(
                    new SparseGeneratorDiagnostic(
                        SparseDiagnosticIds.InvalidKeyedInterface,
                        element.Locations.FirstOrDefault(),
                        element.Name
                    )
                );
            }
        }
    }

    private static void CollectCompositeDiagnostics(
        INamedTypeSymbol element,
        AttributeData attribute,
        SparseGeneratorConfig config,
        ImmutableArray<SparseGeneratorDiagnostic>.Builder diagnostics,
        CancellationToken cancellationToken
    )
    {
        if (!TryExtractComponentNames(attribute, out var names))
        {
            diagnostics.Add(
                new SparseGeneratorDiagnostic(
                    SparseDiagnosticIds.InvalidKeyAttributeShape,
                    AttributeLocation(attribute, element),
                    element.Name
                )
            );
            return;
        }

        var readable = GetReadablePropertiesByName(element, cancellationToken);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seen.Add(name))
            {
                diagnostics.Add(
                    new SparseGeneratorDiagnostic(
                        SparseDiagnosticIds.DuplicateKeyComponent,
                        AttributeLocation(attribute, element),
                        element.Name + "." + name
                    )
                );
                continue;
            }

            if (!readable.TryGetValue(name, out var property))
            {
                var offender = element.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault();
                diagnostics.Add(
                    new SparseGeneratorDiagnostic(
                        offender is null
                            ? SparseDiagnosticIds.MissingKeyComponent
                            : SparseDiagnosticIds.InaccessibleKeyProperty,
                        (offender?.Locations.FirstOrDefault())
                            ?? AttributeLocation(attribute, element),
                        element.Name + "." + name
                    )
                );
                continue;
            }

            AddKeyTypeDiagnostic(
                element,
                property,
                property.Type,
                config,
                diagnostics,
                cancellationToken
            );
        }
    }

    private static void CollectInterfaceDiagnostics(
        INamedTypeSymbol element,
        KeyMechanisms mechanisms,
        SparseGeneratorConfig config,
        ImmutableArray<SparseGeneratorDiagnostic>.Builder diagnostics,
        CancellationToken cancellationToken
    )
    {
        var distinctKeys = mechanisms
            .KeyedInterfaces.Select(static match => match.TypeArguments[0])
            .Distinct(
                (System.Collections.Generic.IEqualityComparer<Microsoft.CodeAnalysis.ITypeSymbol>)
                    SymbolEqualityComparer.Default
            )
            .ToArray();
        if (distinctKeys.Length != 1 || distinctKeys[0] is IErrorTypeSymbol)
        {
            diagnostics.Add(
                new SparseGeneratorDiagnostic(
                    SparseDiagnosticIds.InvalidKeyedInterface,
                    element.Locations.FirstOrDefault(),
                    element.Name
                )
            );
            return;
        }

        var keyType = distinctKeys[0];
        var problem = GetKeyTypeProblem(keyType, config, cancellationToken);
        if (problem == KeyTypeProblem.Nullable)
        {
            diagnostics.Add(
                new SparseGeneratorDiagnostic(
                    SparseDiagnosticIds.NullableKey,
                    element.Locations.FirstOrDefault(),
                    element.Name
                )
            );
            return;
        }

        if (problem == KeyTypeProblem.Collection)
        {
            diagnostics.Add(
                new SparseGeneratorDiagnostic(
                    SparseDiagnosticIds.UnsupportedKeyShape,
                    element.Locations.FirstOrDefault(),
                    element.Name
                )
            );
            return;
        }

        if (!HasPublicReadableSparseKey(element, cancellationToken))
        {
            diagnostics.Add(
                new SparseGeneratorDiagnostic(
                    SparseDiagnosticIds.InvalidKeyedInterface,
                    element.Locations.FirstOrDefault(),
                    element.Name
                )
            );
        }
    }

    private static void AddKeyTypeDiagnostic(
        INamedTypeSymbol element,
        IPropertySymbol property,
        ITypeSymbol keyType,
        SparseGeneratorConfig config,
        ImmutableArray<SparseGeneratorDiagnostic>.Builder diagnostics,
        CancellationToken cancellationToken
    )
    {
        switch (GetKeyTypeProblem(keyType, config, cancellationToken))
        {
            case KeyTypeProblem.Nullable:
                diagnostics.Add(
                    new SparseGeneratorDiagnostic(
                        SparseDiagnosticIds.NullableKey,
                        property.Locations.FirstOrDefault(),
                        element.Name + "." + property.Name
                    )
                );
                break;
            case KeyTypeProblem.Collection:
                diagnostics.Add(
                    new SparseGeneratorDiagnostic(
                        SparseDiagnosticIds.UnsupportedKeyShape,
                        property.Locations.FirstOrDefault(),
                        element.Name + "." + property.Name
                    )
                );
                break;
        }
    }

    private enum KeyTypeProblem
    {
        Nullable,
        Collection,
    }

    private static KeyTypeProblem? GetKeyTypeProblem(
        ITypeSymbol type,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (
            type.NullableAnnotation == NullableAnnotation.Annotated
            || (
                type is INamedTypeSymbol namedNullable
                && namedNullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            )
        )
        {
            return KeyTypeProblem.Nullable;
        }

        if (type is IArrayTypeSymbol)
        {
            return KeyTypeProblem.Collection;
        }

        if (
            type is INamedTypeSymbol named
            && SparseCollectionAnalyzer.GetCollectionInfo(named).CloneKind
                != SparseCloneCollectionKind.Unsupported
        )
        {
            // Strings and other scalar framework types are not collections.
            return KeyTypeProblem.Collection;
        }

        _ = config;
        return null;
    }

    private static bool HasAttributeArguments(AttributeData attribute) =>
        attribute.ConstructorArguments.Length > 0 || attribute.NamedArguments.Length > 0;

    private static bool TryExtractComponentNames(
        AttributeData attribute,
        out ImmutableArray<string> names
    )
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var argument in attribute.ConstructorArguments)
        {
            if (argument.Kind == TypedConstantKind.Array)
            {
                foreach (var value in argument.Values)
                {
                    if (value.Value is string name)
                    {
                        builder.Add(name);
                    }
                }
            }
            else if (argument.Value is string name)
            {
                builder.Add(name);
            }
            else if (argument.Kind != TypedConstantKind.Error)
            {
                names = ImmutableArray<string>.Empty;
                return false;
            }
        }

        names = builder.ToImmutable();
        if (names.Length == 0)
        {
            return false;
        }

        // Empty names can never resolve; keep them so they report as missing components.
        return true;
    }

    private static Dictionary<string, IPropertySymbol> GetReadablePropertiesByName(
        INamedTypeSymbol element,
        CancellationToken cancellationToken
    )
    {
        var readable = new Dictionary<string, IPropertySymbol>(StringComparer.Ordinal);
        foreach (
            var property in SparseModelDiscovery.GetReadableProperties(element, cancellationToken)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            readable[property.Name] = property;
        }

        return readable;
    }

    private static bool IsPubliclyReadableInstance(IPropertySymbol property) =>
        !property.IsStatic
        && !property.IsIndexer
        && property.DeclaredAccessibility == Accessibility.Public
        && property.GetMethod?.DeclaredAccessibility == Accessibility.Public;

    private static Location? AttributeLocation(
        AttributeData attribute,
        INamedTypeSymbol fallback
    ) =>
        attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
        ?? fallback.Locations.FirstOrDefault();

    private static string NonNullableTypeName(ITypeSymbol type) =>
        type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
