using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Generator.Shared;

internal readonly record struct SparseTypeModel(
    string Name,
    string NonNullableName,
    string RuntimeName,
    bool IsReferenceType,
    bool IsFragmentModel,
    string? PocoCloneHelperName,
    string PatchApiPrefix = "",
    bool UsesDefaultScalarEquality = false,
    string? ObservableTypeName = null,
    string? ReadOnlyViewTypeName = null,
    string? PocoReadOnlyViewKey = null
);

internal readonly record struct SparsePropertyModel(
    string Name,
    SparseTypeModel Type,
    bool IsInitOnly = false,
    bool IsRequired = false,
    bool IsReadOnly = false,
    string? JsonPropertyName = null,
    bool HasExplicitJsonPropertyName = false,
    int JsonIgnoreCondition = 0,
    bool IsNullable = false,
    string? AttributeExpressions = null
)
{
    public bool IsJsonIgnored => JsonIgnoreCondition == 1;

    public bool IsJsonIgnoreWhenWritingNull => JsonIgnoreCondition == 3;

    public bool IsJsonIgnoreWhenWritingDefault => JsonIgnoreCondition == 2;
}

internal readonly record struct SparseCollectionInfo(
    SparseCollectionKind Kind,
    SparseCloneCollectionKind CloneKind,
    SparseTypeModel ElementType,
    SparseTypeModel? ValueType,
    string? NamedTypeDefinition,
    SparseCollectionSemantic Semantic = default,
    ImmutableArray<string> KeyPropertyNames = default,
    string? KeyTypeName = null,
    SparseKeyKind KeyKind = default,
    string? UnassignedKeyExpression = null
)
{
    public static SparseCollectionInfo Unsupported { get; } =
        new(
            SparseCollectionKind.Unsupported,
            SparseCloneCollectionKind.Unsupported,
            default,
            null,
            null,
            SparseCollectionSemantic.None,
            ImmutableArray<string>.Empty,
            null,
            SparseKeyKind.None,
            null
        );

    /// <summary>Sequence of scalar (non-structural) elements; needs no key.</summary>
    public bool IsScalarSequence => Semantic == SparseCollectionSemantic.ScalarSequence;

    /// <summary>Structural sequence with a usable discovered key.</summary>
    public bool IsKeyedSequence =>
        Semantic == SparseCollectionSemantic.KeyedSequence && KeyKind != SparseKeyKind.None;

    /// <summary>Dictionary keyed by <c>TKey</c> (<see cref="ElementType"/>).</summary>
    public bool IsDictionary => Semantic == SparseCollectionSemantic.Dictionary;

    /// <summary>More than one key property (tuple key).</summary>
    public bool IsCompositeKey => KeyKind == SparseKeyKind.Composite;
}

internal sealed class SparseSymbolMemberModel(
    int id,
    IPropertySymbol property,
    INamedTypeSymbol? childModel,
    int mergeMode,
    SparseSymbolCollectionInfo collection,
    INamedTypeSymbol? mergeStrategyType,
    bool hasExplicitMergeMode,
    INamedTypeSymbol? rebasePolicyType = null,
    INamedTypeSymbol? comparisonComparerType = null
)
{
    public int Id { get; } = id;
    public IPropertySymbol Property { get; } = property;
    public INamedTypeSymbol? ChildModel { get; } = childModel;
    public int MergeMode { get; } = mergeMode;
    public SparseSymbolCollectionInfo Collection { get; } = collection;
    public INamedTypeSymbol? MergeStrategyType { get; } = mergeStrategyType;
    public bool HasExplicitMergeMode { get; } = hasExplicitMergeMode;
    public INamedTypeSymbol? RebasePolicyType { get; } = rebasePolicyType;
    public INamedTypeSymbol? ComparisonComparerType { get; } = comparisonComparerType;
}

internal readonly record struct SparseMemberModel(
    int Id,
    SparsePropertyModel Property,
    SparseTypeModel? ChildModel,
    int MergeMode,
    SparseCollectionInfo Collection,
    SparseTypeModel? MergeStrategyType,
    string? ChildFragmentType,
    bool ChildIsStructural,
    bool ChildIsReferenceType,
    bool PortableSetView = false,
    bool HasExplicitMergeMode = false,
    bool RedactBefore = false,
    SparseTypeModel? RebasePolicyType = null,
    SparseTypeModel? ComparisonComparerType = null
);

internal readonly record struct SparseModelInfo(
    string Name,
    string ModelTypeName,
    string Namespace,
    bool IsGlobalNamespace,
    bool IsStruct,
    bool IsRecord,
    string HintName,
    ModelConstructorBinding? Constructor,
    ImmutableArray<string> IgnoredSettablePropertyNames = default,
    bool IsPublic = true
)
{
    public bool Equals(SparseModelInfo other) =>
        Name == other.Name
        && ModelTypeName == other.ModelTypeName
        && Namespace == other.Namespace
        && IsGlobalNamespace == other.IsGlobalNamespace
        && IsStruct == other.IsStruct
        && IsRecord == other.IsRecord
        && IsPublic == other.IsPublic
        && HintName == other.HintName
        && Equals(Constructor, other.Constructor)
        && SparseSequence.Equal(IgnoredSettablePropertyNames, other.IgnoredSettablePropertyNames);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Name.GetHashCode();
            hash = hash * 31 + ModelTypeName.GetHashCode();
            hash = hash * 31 + Namespace.GetHashCode();
            hash = hash * 31 + (IsGlobalNamespace ? 1 : 0);
            hash = hash * 31 + (IsStruct ? 1 : 0);
            hash = hash * 31 + (IsRecord ? 1 : 0);
            hash = hash * 31 + (IsPublic ? 1 : 0);
            hash = hash * 31 + HintName.GetHashCode();
            hash = hash * 31 + (Constructor?.GetHashCode() ?? 0);
            return hash * 31 + SparseSequence.Hash(IgnoredSettablePropertyNames);
        }
    }
}

internal sealed record SparseStructuralModel(
    string HostName,
    string ValueTypeName,
    ImmutableArray<SparseMemberModel> Members,
    ModelConstructorBinding? Constructor
)
{
    public bool Equals(SparseStructuralModel? other) =>
        other is not null
        && HostName == other.HostName
        && ValueTypeName == other.ValueTypeName
        && Equals(Constructor, other.Constructor)
        && SparseSequence.Equal(Members, other.Members);

    public override int GetHashCode() =>
        unchecked(
            (HostName.GetHashCode() * 31 + ValueTypeName.GetHashCode()) * 31
            + SparseSequence.Hash(Members)
            + (Constructor?.GetHashCode() ?? 0)
        );
}

internal sealed record SparsePocoCloneModel(
    SparseModelInfo Model,
    string CloneHelperName,
    ImmutableArray<SparseMemberModel> Members
)
{
    public bool Equals(SparsePocoCloneModel? other) =>
        other is not null
        && Model == other.Model
        && CloneHelperName == other.CloneHelperName
        && SparseSequence.Equal(Members, other.Members);

    public override int GetHashCode() =>
        unchecked(
            (Model.GetHashCode() * 31 + CloneHelperName.GetHashCode()) * 31
            + SparseSequence.Hash(Members)
        );
}

internal sealed record SparseReadOnlyViewModel(
    string Key,
    string Name,
    string SourceTypeName,
    ImmutableArray<SparseMemberModel> Members,
    bool IsOpaque = false,
    bool StoresValue = true
)
{
    public bool Equals(SparseReadOnlyViewModel? other) =>
        other is not null
        && Key == other.Key
        && Name == other.Name
        && SourceTypeName == other.SourceTypeName
        && IsOpaque == other.IsOpaque
        && StoresValue == other.StoresValue
        && SparseSequence.Equal(Members, other.Members);

    public override int GetHashCode()
    {
        var hash = Key.GetHashCode();
        hash = unchecked(hash * 31 + Name.GetHashCode());
        hash = unchecked(hash * 31 + SourceTypeName.GetHashCode());
        hash = unchecked(hash * 31 + (IsOpaque ? 1 : 0));
        hash = unchecked(hash * 31 + (StoresValue ? 1 : 0));
        return unchecked(hash * 31 + SparseSequence.Hash(Members));
    }
}

internal sealed record SparsePromotedModel(
    SparseModelInfo Model,
    ImmutableArray<SparseMemberModel> Members,
    ImmutableArray<SparsePocoCloneModel> PocoCloneModels,
    ImmutableArray<SparseStructuralModel> StructuralModels,
    ImmutableArray<SparseReadOnlyViewModel> ReadOnlyViewModels = default
)
{
    public bool Equals(SparsePromotedModel? other) =>
        other is not null
        && Model.Equals(other.Model)
        && SparseSequence.Equal(Members, other.Members)
        && SparseSequence.Equal(PocoCloneModels, other.PocoCloneModels)
        && SparseSequence.Equal(StructuralModels, other.StructuralModels)
        && SparseSequence.Equal(ReadOnlyViewModels, other.ReadOnlyViewModels);

    public override int GetHashCode()
    {
        var hash = Model.GetHashCode();
        hash = unchecked(hash * 31 + SparseSequence.Hash(Members));
        hash = unchecked(hash * 31 + SparseSequence.Hash(PocoCloneModels));
        hash = unchecked(hash * 31 + SparseSequence.Hash(StructuralModels));
        return unchecked(hash * 31 + SparseSequence.Hash(ReadOnlyViewModels));
    }
}

internal sealed record SparseGenerationAnalysis(
    SparseModelInfo? Model,
    ImmutableArray<SparseMemberModel> Members,
    ImmutableArray<SparsePocoCloneModel> PocoCloneModels,
    ImmutableArray<SparseStructuralModel> StructuralModels,
    ImmutableArray<SparseGeneratorDiagnostic> Diagnostics,
    ImmutableArray<SparsePromotedModel> PromotedModels = default,
    ImmutableArray<SparseReadOnlyViewModel> ReadOnlyViewModels = default
)
{
    public bool Equals(SparseGenerationAnalysis? other) =>
        other is not null
        && Model == other.Model
        && SparseSequence.Equal(Members, other.Members)
        && SparseSequence.Equal(PocoCloneModels, other.PocoCloneModels)
        && SparseSequence.Equal(StructuralModels, other.StructuralModels)
        && SparseSequence.Equal(Diagnostics, other.Diagnostics)
        && SparseSequence.Equal(PromotedModels, other.PromotedModels)
        && SparseSequence.Equal(ReadOnlyViewModels, other.ReadOnlyViewModels);

    public override int GetHashCode()
    {
        var hash = Model?.GetHashCode() ?? 0;
        hash = unchecked(hash * 31 + SparseSequence.Hash(Members));
        hash = unchecked(hash * 31 + SparseSequence.Hash(PocoCloneModels));
        hash = unchecked(hash * 31 + SparseSequence.Hash(StructuralModels));
        hash = unchecked(hash * 31 + SparseSequence.Hash(Diagnostics));
        hash = unchecked(hash * 31 + SparseSequence.Hash(PromotedModels));
        return unchecked(hash * 31 + SparseSequence.Hash(ReadOnlyViewModels));
    }
}

internal sealed record SparseGenerationResult(
    string? HintName,
    string? Source,
    ImmutableArray<SparseGeneratorDiagnostic> Diagnostics
)
{
    public bool Equals(SparseGenerationResult? other) =>
        other is not null
        && HintName == other.HintName
        && Source == other.Source
        && SparseSequence.Equal(Diagnostics, other.Diagnostics);

    public override int GetHashCode() =>
        unchecked(
            ((HintName?.GetHashCode() ?? 0) * 31 + (Source?.GetHashCode() ?? 0)) * 31
            + SparseSequence.Hash(Diagnostics)
        );
}

internal static class SparseSequence
{
    public static bool Equal<T>(ImmutableArray<T> left, ImmutableArray<T> right) =>
        left.IsDefault || right.IsDefault
            ? left.IsDefault == right.IsDefault
            : left.SequenceEqual(right);

    public static int Hash<T>(ImmutableArray<T> values)
    {
        if (values.IsDefault)
            return 0;
        var hash = 17;
        foreach (var value in values)
        {
            hash = unchecked(
                hash * 31 + (value is null ? 0 : EqualityComparer<T>.Default.GetHashCode(value))
            );
        }
        return hash;
    }
}

internal readonly record struct SparseGeneratorDiagnostic(
    string DescriptorId,
    Location? Location,
    ImmutableArray<string?> Arguments
)
{
    public SparseGeneratorDiagnostic(string descriptorId, Location? location, string? argument)
        : this(
            descriptorId,
            location,
            argument is null
                ? ImmutableArray<string?>.Empty
                : ImmutableArray.Create<string?>(argument)
        ) { }

    public string? Argument1 => Arguments.Length > 0 ? Arguments[0] : null;

    public SparseLocationSnapshot? Snapshot => SparseLocationSnapshot.Capture(Location);

    public SparseDiagnosticPayload ToPayload() => SparseDiagnosticPayload.FromDiagnostic(this);

    // ImmutableArray<T> equality is reference-based, so structural comparison
    // goes through SparseSequence like every other shared model.
    public bool Equals(SparseGeneratorDiagnostic other) =>
        string.Equals(DescriptorId, other.DescriptorId, StringComparison.Ordinal)
        && Equals(Location, other.Location)
        && SparseSequence.Equal(Arguments, other.Arguments);

    public override int GetHashCode() =>
        unchecked(
            (StringComparer.Ordinal.GetHashCode(DescriptorId) * 31 + (Location?.GetHashCode() ?? 0))
                * 31
            + SparseSequence.Hash(Arguments)
        );
}
