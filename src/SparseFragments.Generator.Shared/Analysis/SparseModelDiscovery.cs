using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace SparseFragments.Generator.Shared;

/// <summary>Discovers model members and builds product-neutral generation models.</summary>
internal static class SparseModelDiscovery
{
    internal static IEnumerable<IPropertySymbol> GetReadableProperties(
        INamedTypeSymbol model,
        SparseGeneratorConfig? config,
        CancellationToken cancellationToken,
        bool requirePublicSetter = false,
        bool includeSparseIgnored = false
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

        var properties = new Dictionary<string, IPropertySymbol>(StringComparer.Ordinal);
        while (hierarchy.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var property in hierarchy.Pop().GetMembers().OfType<IPropertySymbol>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    property.IsStatic
                    || property.IsIndexer
                    || property.DeclaredAccessibility != Accessibility.Public
                    || property.GetMethod?.DeclaredAccessibility != Accessibility.Public
                    || (
                        !includeSparseIgnored
                        && config is not null
                        && IsSparseIgnored(property, config)
                    )
                    || (
                        requirePublicSetter
                        && property.SetMethod?.DeclaredAccessibility != Accessibility.Public
                    )
                )
                {
                    continue;
                }

                properties[property.Name] = property;
            }
        }

        return properties.Values.OrderBy(static property => property.Name, StringComparer.Ordinal);
    }

    internal static bool IsSparseIgnored(IPropertySymbol property, SparseGeneratorConfig config) =>
        property
            .GetAttributes()
            .Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == config.IgnoreAttributeMetadataName
            );

    internal static IEnumerable<SparseSymbolMemberModel> GetMembers(
        INamedTypeSymbol model,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var constructor = IsFragmentModel(model, config, cancellationToken)
            ? ModelConstructorBinding.AnalyzeRoot(model, config, cancellationToken)
            : ModelConstructorBinding.AnalyzeStructural(model, config, cancellationToken);
        var index = 0;
        foreach (
            var property in GetReadableProperties(model, config, cancellationToken)
                .Where(property =>
                    property.SetMethod?.DeclaredAccessibility == Accessibility.Public
                    || (
                        property.SetMethod is null
                        && constructor is not null
                        && constructor.Parameters.Any(parameter =>
                            parameter.PropertyName == property.Name
                        )
                    )
                )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var child =
                IsFragmentModel(property.Type, config, cancellationToken)
                || IsStructuralType(property.Type, config, cancellationToken)
                || (
                    property.Type is INamedTypeSymbol namedChild
                    && SparsePromotedDiscovery.IsPromotablePartial(
                        namedChild,
                        config,
                        cancellationToken
                    )
                )
                    ? (INamedTypeSymbol)property.Type
                    : null;
            // Omitted attributes select the shape-aware Default; explicit
            // Replace opts out of deep/keyed semantics.
            var mode = SparseMergeModes.Default;
            AttributeData? merge = null;
            foreach (var attribute in property.GetAttributes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    attribute.AttributeClass?.ToDisplayString() == config.MergeAttributeMetadataName
                )
                {
                    merge = attribute;
                    break;
                }
            }

            INamedTypeSymbol? mergeStrategyType = null;
            var hasExplicitMergeMode = merge is not null;
            if (
                merge?.ConstructorArguments.FirstOrDefault() is
                { Kind: TypedConstantKind.Type } strategyConstant
            )
            {
                mergeStrategyType = strategyConstant.Value as INamedTypeSymbol;
                mode = SparseMergeModes.Custom;
            }
            else if (merge?.ConstructorArguments.FirstOrDefault().Value is int requestedMode)
            {
                mode = config.EffectiveMergeModeMap.Normalize(requestedMode);
            }

            INamedTypeSymbol? rebasePolicyType = null;
            if (config.RebasePolicyAttributeMetadataName is not null)
            {
                foreach (var attribute in property.GetAttributes())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (
                        attribute.AttributeClass?.ToDisplayString()
                            == config.RebasePolicyAttributeMetadataName
                        && attribute.ConstructorArguments.FirstOrDefault()
                            is { Kind: TypedConstantKind.Type } policyConstant
                    )
                    {
                        rebasePolicyType = policyConstant.Value as INamedTypeSymbol;
                        break;
                    }
                }
            }

            yield return new SparseSymbolMemberModel(
                index++,
                property,
                child,
                mode,
                SparseCollectionAnalyzer.GetCollectionInfo(property.Type),
                mergeStrategyType,
                hasExplicitMergeMode,
                rebasePolicyType
            );
        }
    }

    internal static bool IsFragmentModel(
        ITypeSymbol type,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        if (
            type
            is not INamedTypeSymbol
            {
                TypeKind: TypeKind.Class or TypeKind.Struct,
                IsAbstract: false,
            } named
        )
        {
            return false;
        }

        foreach (var attribute in named.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attribute.AttributeClass?.ToDisplayString() == config.ModelAttributeMetadataName)
            {
                return true;
            }
        }

        return false;
    }

    internal enum StructuralTypeKind
    {
        RootModel,
        StructuralObject,
        Collection,
        Scalar,
    }

    internal static StructuralTypeKind ClassifyStructuralType(
        ITypeSymbol type,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        if (
            type is IArrayTypeSymbol
            || (
                type is INamedTypeSymbol arrayLike
                && SparseCollectionAnalyzer.GetCollectionInfo(arrayLike).Kind
                    != SparseCollectionKind.Unsupported
            )
        )
        {
            return StructuralTypeKind.Collection;
        }

        if (
            type
                is not INamedTypeSymbol
                {
                    TypeKind: TypeKind.Class,
                    IsAbstract: false,
                    Arity: 0,
                } named
            || named.SpecialType != SpecialType.None
            || IsFrameworkType(named)
            || ModelConstructorBinding.AnalyzeStructural(named, config, cancellationToken) is null
        )
        {
            return StructuralTypeKind.Scalar;
        }

        if (IsFragmentModel(named, config, cancellationToken))
        {
            return StructuralTypeKind.RootModel;
        }

        if (
            HasUnsupportedPocoMembers(named, config, cancellationToken)
            || !GetReadableProperties(named, config, cancellationToken).Any()
        )
        {
            return StructuralTypeKind.Scalar;
        }

        return StructuralTypeKind.StructuralObject;
    }

    internal static bool IsStructuralType(
        ITypeSymbol type,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        // Non-partial nested POCOs are atomic replace values.
        if (config.StructuralPolicy != SparseStructuralPolicy.StructuralHosts)
        {
            return false;
        }

        return ClassifyStructuralType(type, config, cancellationToken)
            == StructuralTypeKind.StructuralObject;
    }

    internal static bool IsFrameworkType(INamedTypeSymbol type)
    {
        var namespaceName = type.ContainingNamespace.ToDisplayString();
        if (
            namespaceName == "System"
            || namespaceName.StartsWith("System.", StringComparison.Ordinal)
            || namespaceName == "Microsoft"
            || namespaceName.StartsWith("Microsoft.", StringComparison.Ordinal)
        )
        {
            return true;
        }

        var assemblyName = type.ContainingAssembly?.Name;
        return assemblyName is not null
            && (
                assemblyName.StartsWith("System.", StringComparison.Ordinal)
                || assemblyName.StartsWith("Microsoft.", StringComparison.Ordinal)
            );
    }

    internal static bool IsAccessibleForGeneration(INamedTypeSymbol type)
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

    private static bool IsAccessibleForClone(INamedTypeSymbol type)
    {
        if (type.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
        {
            return false;
        }

        for (
            var current = type.ContainingType;
            current is not null;
            current = current.ContainingType
        )
        {
            if (
                current.DeclaredAccessibility
                is not (Accessibility.Public or Accessibility.Internal)
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetPocoCloneType(
        ITypeSymbol type,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken,
        out INamedTypeSymbol pocoType
    )
    {
        if (
            type is INamedTypeSymbol named
            && SparsePromotedDiscovery.IsPromotablePartial(named, config, cancellationToken)
        )
        {
            pocoType = null!;
            return false;
        }

        if (
            type is INamedTypeSymbol namedType
            && ClassifyStructuralType(type, config, cancellationToken)
                == StructuralTypeKind.StructuralObject
            && IsAccessibleForClone(namedType)
        )
        {
            var members = GetMembers(namedType, config, cancellationToken).ToArray();
            if (members.Length == 0)
            {
                pocoType = null!;
                return false;
            }

            pocoType = namedType;
            return true;
        }

        pocoType = null!;
        return false;
    }

    private static string StructuralHostName(
        INamedTypeSymbol type,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var name = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            .ToDisplayString(SparseNaming.TypeFormat);
        var assembly = type.ContainingAssembly?.Name ?? string.Empty;
        return config.StructuralHostPrefix
            + SparseNaming.GetStableTypeHash(assembly + "|" + name, cancellationToken);
    }

    private static bool HasUnsupportedPocoMembers(
        INamedTypeSymbol pocoType,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    ) => ModelConstructionPlan.HasUnsupportedStructuralMembers(pocoType, config, cancellationToken);

    internal static ImmutableArray<INamedTypeSymbol> GetPocoCloneTypes(
        ImmutableArray<SparseSymbolMemberModel> members,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var result = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var pending = new Stack<ITypeSymbol>();
        foreach (var type in members.Select(static member => member.Property.Type))
        {
            pending.Push(type);
        }

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var type = pending.Pop();
            var collection = SparseCollectionAnalyzer.GetCollectionInfo(type);
            if (collection.CloneKind != SparseCloneCollectionKind.Unsupported)
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

            if (
                !TryGetPocoCloneType(type, config, cancellationToken, out var poco)
                || !seen.Add(poco)
            )
            {
                continue;
            }

            result.Add(poco);
            foreach (
                var nestedType in GetMembers(poco, config, cancellationToken)
                    .Select(static member => member.Property.Type)
            )
            {
                pending.Push(nestedType);
            }
        }

        return result.ToImmutable();
    }

    internal static ImmutableArray<INamedTypeSymbol> CollectStructuralTypes(
        ImmutableArray<SparseSymbolMemberModel> members,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var result = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var pending = new Stack<INamedTypeSymbol>();
        foreach (var child in members.Select(static member => member.ChildModel))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                child is not null
                && IsStructuralType(child, config, cancellationToken)
                && !SparsePromotedDiscovery.IsPromotablePartial(child, config, cancellationToken)
            )
            {
                pending.Push(child);
            }
        }

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var type = pending.Pop();
            if (!seen.Add(type))
            {
                continue;
            }

            result.Add(type);
            foreach (
                var nested in GetMembers(type, config, cancellationToken)
                    .Select(static member => member.ChildModel)
            )
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    nested is not null
                    && IsStructuralType(nested, config, cancellationToken)
                    && !SparsePromotedDiscovery.IsPromotablePartial(
                        nested,
                        config,
                        cancellationToken
                    )
                )
                {
                    pending.Push(nested);
                }
            }
        }

        return result.ToImmutable();
    }

    internal static SparseStructuralModel CreateStructuralModel(
        INamedTypeSymbol type,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    ) =>
        new(
            StructuralHostName(type, config, cancellationToken),
            SparseNaming.NonNullableTypeName(type),
            CreateMemberModels(
                GetMembers(type, config, cancellationToken).ToImmutableArray(),
                config,
                cancellationToken
            ),
            ModelConstructorBinding.AnalyzeStructural(type, config, cancellationToken)
        );

    internal static ImmutableArray<SparseMemberModel> CreateMemberModels(
        ImmutableArray<SparseSymbolMemberModel> members,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var result = ImmutableArray.CreateBuilder<SparseMemberModel>(members.Length);
        foreach (var member in members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(CreateMemberModel(member, config, cancellationToken));
        }

        return result.ToImmutable();
    }

    private static SparseMemberModel CreateMemberModel(
        SparseSymbolMemberModel member,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var jsonPropertyName = SparseJsonNaming.GetJsonPropertyName(
            member.Property,
            cancellationToken,
            out var hasExplicitJsonPropertyName
        );
        var property = new SparsePropertyModel(
            member.Property.Name,
            CreateTypeModel(member.Property.Type, config, cancellationToken),
            member.Property.SetMethod?.IsInitOnly == true,
            RoslynSymbolCompat.IsRequired(member.Property),
            member.Property.SetMethod is null,
            jsonPropertyName,
            hasExplicitJsonPropertyName,
            SparseJsonNaming.GetJsonIgnoreCondition(member.Property, cancellationToken),
            IsNullableType(member.Property.Type)
        );
        SparseTypeModel? childModel = null;
        string? childFragmentType = null;
        var childIsStructural = false;
        var childIsReferenceType = true;
        if (member.ChildModel is not null)
        {
            childModel = CreateTypeModel(member.ChildModel, config, cancellationToken);
            childIsReferenceType = member.ChildModel.IsReferenceType;
            var isPromoted = SparsePromotedDiscovery.IsPromotablePartial(
                member.ChildModel,
                config,
                cancellationToken
            );
            childIsStructural =
                !isPromoted && !IsFragmentModel(member.ChildModel, config, cancellationToken);
            var host = childIsStructural
                ? StructuralHostName(member.ChildModel, config, cancellationToken)
                : SparseNaming.NonNullableTypeName(member.ChildModel);
            childFragmentType = host + "." + SparseWellKnownNames.FragmentTypeName;
        }

        SparseTypeModel? mergeStrategyType = null;
        if (member.MergeStrategyType is not null)
        {
            mergeStrategyType = CreateTypeModel(
                member.MergeStrategyType,
                config,
                cancellationToken
            );
        }

        SparseTypeModel? rebasePolicyType = null;
        if (member.RebasePolicyType is not null)
        {
            rebasePolicyType = CreateTypeModel(member.RebasePolicyType, config, cancellationToken);
        }

        return new SparseMemberModel(
            member.Id,
            property,
            childModel,
            member.MergeMode,
            CreateCollectionInfo(member.Collection, config, cancellationToken),
            mergeStrategyType,
            childFragmentType,
            childIsStructural,
            childIsReferenceType,
            HasExplicitMergeMode: member.HasExplicitMergeMode,
            RebasePolicyType: rebasePolicyType
        );
    }

    private static bool IsNullableType(ITypeSymbol type)
    {
        if (type.NullableAnnotation == NullableAnnotation.Annotated)
        {
            return true;
        }

        return type is INamedTypeSymbol named
            && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
    }

    private static SparseCollectionInfo CreateCollectionInfo(
        SparseSymbolCollectionInfo collection,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        if (
            collection.Kind == SparseCollectionKind.Unsupported
            && collection.CloneKind == SparseCloneCollectionKind.Unsupported
        )
        {
            return SparseCollectionInfo.Unsupported;
        }

        SparseTypeModel? valueType = null;
        if (collection.ValueType is not null)
        {
            valueType = CreateTypeModel(collection.ValueType, config, cancellationToken);
        }

        var semantic = SparseCollectionAnalyzer.ClassifySemantic(
            collection,
            config,
            cancellationToken
        );
        var keyPropertyNames = ImmutableArray<string>.Empty;
        string? keyTypeName = null;
        var keyKind = SparseKeyKind.None;
        string? unassignedKeyExpression = null;
        if (
            semantic == SparseCollectionSemantic.KeyedSequence
            && collection.ElementType is INamedTypeSymbol namedElement
            && SparseCollectionAnalyzer.TryDiscoverKeyInfo(
                namedElement,
                config,
                cancellationToken,
                out var discovered
            )
            && discovered is not null
        )
        {
            keyPropertyNames = discovered.PropertyNames;
            keyTypeName = discovered.KeyTypeName;
            keyKind = discovered.Kind;
            unassignedKeyExpression = discovered.UnassignedKeyExpression;
        }

        return new SparseCollectionInfo(
            collection.Kind,
            collection.CloneKind,
            CreateTypeModel(collection.ElementType, config, cancellationToken),
            valueType,
            collection.NamedType?.ConstructedFrom.ToDisplayString(),
            semantic,
            keyPropertyNames,
            keyTypeName,
            keyKind,
            unassignedKeyExpression
        );
    }

    private static SparseTypeModel CreateTypeModel(
        ITypeSymbol type,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var isFragmentModel = IsFragmentModel(type, config, cancellationToken);
        if (
            !isFragmentModel
            && type is INamedTypeSymbol promotable
            && SparsePromotedDiscovery.IsPromotablePartial(promotable, config, cancellationToken)
        )
        {
            isFragmentModel = true;
        }

        string? pocoCloneHelperName = null;
        if (
            !isFragmentModel
            && TryGetPocoCloneType(type, config, cancellationToken, out var pocoType)
        )
        {
            var cloneTypeName = pocoType
                .WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            pocoCloneHelperName =
                SparseWellKnownNames.CloneHelperPrefix
                + SparseNaming.GetStableTypeHash(cloneTypeName, cancellationToken);
        }

        return new SparseTypeModel(
            SparseNaming.TypeName(type),
            SparseNaming.NonNullableTypeName(type),
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            type.IsReferenceType,
            isFragmentModel,
            pocoCloneHelperName,
            type is INamedTypeSymbol named && (isFragmentModel || pocoCloneHelperName is not null)
                ? SparseNaming.PatchApiPrefix(
                    GetMembers(named, config, cancellationToken)
                        .Select(static member => member.Property.Name)
                )
                : string.Empty,
            UsesDefaultScalarEquality(type),
            GetObservableTypeName(type, isFragmentModel, config, cancellationToken)
        );
    }

    private static string? GetObservableTypeName(
        ITypeSymbol type,
        bool isFragmentModel,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        if (!isFragmentModel || !type.IsReferenceType || type is not INamedTypeSymbol model)
        {
            return null;
        }

        var memberNames = new HashSet<string>(
            GetMembers(model, config, cancellationToken)
                .Select(static member => member.Property.Name),
            StringComparer.Ordinal
        );
        var name = new StringBuilder("Observable");
        while (memberNames.Contains(name.ToString()))
        {
            name.Insert(0, "Sparse");
        }

        return name.ToString();
    }

    private static bool UsesDefaultScalarEquality(ITypeSymbol type)
    {
        if (
            type is INamedTypeSymbol nullable
            && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
        )
        {
            return UsesDefaultScalarEquality(nullable.TypeArguments[0]);
        }
        return type.TypeKind == TypeKind.Enum
            || type.SpecialType
                is SpecialType.System_Boolean
                    or SpecialType.System_Char
                    or SpecialType.System_SByte
                    or SpecialType.System_Byte
                    or SpecialType.System_Int16
                    or SpecialType.System_UInt16
                    or SpecialType.System_Int32
                    or SpecialType.System_UInt32
                    or SpecialType.System_Int64
                    or SpecialType.System_UInt64
                    or SpecialType.System_IntPtr
                    or SpecialType.System_UIntPtr
                    or SpecialType.System_Single
                    or SpecialType.System_Double
                    or SpecialType.System_Decimal
                    or SpecialType.System_DateTime
                    or SpecialType.System_String
            || (
                type is INamedTypeSymbol frameworkScalar
                && frameworkScalar.Name is "DateTimeOffset" or "TimeSpan" or "Guid"
                && frameworkScalar.ContainingNamespace.ToDisplayString() == "System"
                && frameworkScalar.ContainingAssembly.GetTypeByMetadataName("System.Object")
                    is { SpecialType: SpecialType.System_Object } coreObject
                && SymbolEqualityComparer.Default.Equals(
                    coreObject.ContainingAssembly,
                    frameworkScalar.ContainingAssembly
                )
            );
    }

    internal static SparsePocoCloneModel CreatePocoCloneModel(
        INamedTypeSymbol pocoType,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var typeName = pocoType
            .WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return new SparsePocoCloneModel(
            CreateModelInfo(pocoType, string.Empty, config, cancellationToken) with
            {
                Constructor = ModelConstructorBinding.AnalyzeStructural(
                    pocoType,
                    config,
                    cancellationToken
                ),
            },
            SparseWellKnownNames.CloneHelperPrefix
                + SparseNaming.GetStableTypeHash(typeName, cancellationToken),
            CreateMemberModels(
                GetMembers(pocoType, config, cancellationToken).ToImmutableArray(),
                config,
                cancellationToken
            )
        );
    }

    internal static SparseModelInfo CreateModelInfo(
        INamedTypeSymbol model,
        string hintName,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    ) =>
        new(
            model.Name,
            SparseNaming.NonNullableTypeName(model),
            model.ContainingNamespace.ToDisplayString(),
            model.ContainingNamespace.IsGlobalNamespace,
            model.TypeKind == TypeKind.Struct,
            model.IsRecord,
            hintName,
            ModelConstructorBinding.AnalyzeRoot(model, config, cancellationToken),
            GetIgnoredSettablePropertyNames(model, config, cancellationToken)
        );

    private static ImmutableArray<string> GetIgnoredSettablePropertyNames(
        INamedTypeSymbol model,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    ) =>
        GetReadableProperties(model, config, cancellationToken, includeSparseIgnored: true)
            .Where(property =>
                IsSparseIgnored(property, config)
                && property.SetMethod
                    is { DeclaredAccessibility: Accessibility.Public, IsInitOnly: false }
            )
            .Select(static property => property.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToImmutableArray();

    internal static ImmutableArray<SparsePromotedModel> CreatePromotedModels(
        ImmutableArray<SparseSymbolMemberModel> members,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var promotedTypes = SparsePromotedDiscovery.CollectPromotedTypes(
            members,
            config,
            cancellationToken
        );
        var result = ImmutableArray.CreateBuilder<SparsePromotedModel>(promotedTypes.Length);
        foreach (var promoted in promotedTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var promotedMembers = GetMembers(promoted, config, cancellationToken)
                .ToImmutableArray();
            var memberModels = CreateMemberModels(promotedMembers, config, cancellationToken);
            var pocoCloneModels = GetPocoCloneTypes(promotedMembers, config, cancellationToken)
                .Select(pocoType => CreatePocoCloneModel(pocoType, config, cancellationToken))
                .ToImmutableArray();
            var structuralModels = CollectStructuralTypes(
                    promotedMembers,
                    config,
                    cancellationToken
                )
                .Select(type => CreateStructuralModel(type, config, cancellationToken))
                .ToImmutableArray();
            result.Add(
                new SparsePromotedModel(
                    CreateModelInfo(promoted, string.Empty, config, cancellationToken),
                    memberModels,
                    pocoCloneModels,
                    structuralModels
                )
            );
        }

        return result.ToImmutable();
    }
}
