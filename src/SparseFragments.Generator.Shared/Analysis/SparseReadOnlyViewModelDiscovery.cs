using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace SparseFragments.Generator.Shared;

internal static class SparseReadOnlyViewModelDiscovery
{
    internal static ImmutableArray<SparseReadOnlyViewModel> GetReadOnlyViewModels(
        ImmutableArray<SparseSymbolMemberModel> members,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var result = ImmutableArray.CreateBuilder<SparseReadOnlyViewModel>();
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        var pending = new Stack<ITypeSymbol>();
        foreach (var member in members)
        {
            pending.Push(member.Property.Type);
        }

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var type = pending.Pop();
            var collection = SparseCollectionAnalyzer.GetCollectionInfo(type);
            if (
                collection.Kind != SparseCollectionKind.Unsupported
                || collection.CloneKind != SparseCloneCollectionKind.Unsupported
            )
            {
                if (collection.ElementType is not null)
                {
                    pending.Push(collection.ElementType);
                }

                if (collection.ValueType is not null)
                {
                    pending.Push(collection.ValueType);
                }

                continue;
            }

            var key = GetPocoReadOnlyViewKey(type, config, cancellationToken);
            if (key is null || !seen.Add(key))
            {
                continue;
            }

            if (key == SparseWellKnownNames.OpaqueReadOnlyViewKey)
            {
                result.Add(
                    new SparseReadOnlyViewModel(
                        key,
                        "OpaqueValue",
                        "global::System.Object?",
                        ImmutableArray<SparseMemberModel>.Empty,
                        IsOpaque: true
                    )
                );
                continue;
            }

            if (type is not INamedTypeSymbol named)
            {
                continue;
            }

            var properties = SparseModelDiscovery
                .GetReadableProperties(named, config, cancellationToken)
                .ToImmutableArray();
            var isOpaque = properties.IsEmpty || !named.IsReferenceType || named.IsRefLikeType;
            var viewMembers = isOpaque
                ? ImmutableArray<SparseMemberModel>.Empty
                : CreateReadOnlyViewMemberModels(properties, config, cancellationToken);
            result.Add(
                new SparseReadOnlyViewModel(
                    key,
                    named.Name,
                    SparseNaming.NonNullableTypeName(named),
                    viewMembers,
                    IsOpaque: isOpaque,
                    StoresValue: named.IsReferenceType && !named.IsRefLikeType
                )
            );
            foreach (var property in isOpaque ? ImmutableArray<IPropertySymbol>.Empty : properties)
            {
                pending.Push(property.Type);
            }
        }

        return result.ToImmutable();
    }

    internal static string? GetPocoReadOnlyViewKey(
        ITypeSymbol type,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        if (type.TypeKind == TypeKind.TypeParameter)
        {
            return SparseWellKnownNames.OpaqueReadOnlyViewKey;
        }

        if (!type.IsReferenceType)
        {
            if (
                SparseModelDiscovery.UsesDefaultScalarEquality(type)
                || type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } namedStruct
            )
            {
                return null;
            }

            var structName = namedStruct
                .WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return SparseWellKnownNames.ReadOnlyValueViewPrefix
                + SparseNaming.GetStableTypeHash(structName, cancellationToken);
        }

        if (type.TypeKind == TypeKind.Dynamic || type.SpecialType == SpecialType.System_Object)
        {
            return SparseWellKnownNames.OpaqueReadOnlyViewKey;
        }

        if (
            type is not INamedTypeSymbol named
            || named.TypeKind is not (TypeKind.Class or TypeKind.Interface)
        )
        {
            return SparseWellKnownNames.OpaqueReadOnlyViewKey;
        }

        if (
            SparseModelDiscovery.IsFragmentModel(named, config, cancellationToken)
            || SparsePromotedDiscovery.IsPromotablePartial(named, config, cancellationToken)
        )
        {
            return null;
        }

        if (SparseModelDiscovery.IsFrameworkType(named))
        {
            return SparseModelDiscovery.UsesDefaultScalarEquality(type)
                ? null
                : SparseWellKnownNames.OpaqueReadOnlyViewKey;
        }

        if (!IsPubliclyAccessible(named))
        {
            return SparseWellKnownNames.OpaqueReadOnlyViewKey;
        }

        var typeName = named
            .WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return SparseWellKnownNames.ReadOnlyValueViewPrefix
            + SparseNaming.GetStableTypeHash(typeName, cancellationToken);
    }

    private static bool IsPubliclyAccessible(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }

    private static ImmutableArray<SparseMemberModel> CreateReadOnlyViewMemberModels(
        ImmutableArray<IPropertySymbol> properties,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var result = ImmutableArray.CreateBuilder<SparseMemberModel>(properties.Length);
        foreach (var property in properties)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SparseTypeModel? childModel = null;
            if (
                property.Type is INamedTypeSymbol child
                && (
                    SparseModelDiscovery.IsFragmentModel(child, config, cancellationToken)
                    || SparsePromotedDiscovery.IsPromotablePartial(child, config, cancellationToken)
                )
            )
            {
                childModel = SparseModelDiscovery.CreateTypeModel(child, config, cancellationToken);
            }

            var propertyModel = new SparsePropertyModel(
                property.Name,
                SparseModelDiscovery.CreateTypeModel(property.Type, config, cancellationToken),
                property.SetMethod?.IsInitOnly == true,
                RoslynSymbolCompat.IsRequired(property),
                property.SetMethod is null,
                JsonPropertyName: null,
                HasExplicitJsonPropertyName: false,
                JsonIgnoreCondition: 0,
                IsNullable: SparseModelDiscovery.IsNullableType(property.Type)
            );
            var collection = SparseModelDiscovery.CreateCollectionInfo(
                SparseCollectionAnalyzer.GetCollectionInfo(property.Type),
                config,
                cancellationToken
            );
            result.Add(
                new SparseMemberModel(
                    result.Count,
                    propertyModel,
                    childModel,
                    SparseMergeModes.Replace,
                    collection,
                    MergeStrategyType: null,
                    ChildFragmentType: null,
                    ChildIsStructural: false,
                    ChildIsReferenceType: childModel?.IsReferenceType ?? true
                )
            );
        }

        return result.ToImmutable();
    }
}
