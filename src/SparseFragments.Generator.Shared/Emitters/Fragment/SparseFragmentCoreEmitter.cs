using System.Collections.Generic;
using System.Collections.Immutable;

namespace SparseFragments.Generator.Shared;

/// <summary>Product-neutral fragment algebra emission, configured with runtime names.</summary>
/// <remarks>
/// This type is the shared entry point used by the SparseFragments
/// generator. Each generation concept is owned by a focused stage emitter; this
/// class only wires them together so generation concepts share one surface.
/// </remarks>
internal sealed class SparseFragmentCoreEmitter
{
    private readonly SparseFragmentDeclarationEmitter _declaration;
    private readonly SparseFragmentConversionEmitter _conversion;
    private readonly SparseFragmentMergeEmitter _merge;
    private readonly SparseFragmentCloneEmitter _clone;

    public SparseFragmentCoreEmitter(
        string optional,
        string mergeStrategyFieldPrefix,
        string cloneContext,
        string referenceComparer,
        SparseFragmentExpressions expressions,
        string? rebasePolicyFieldPrefix = null,
        string fieldQualifier = ""
    )
    {
        _declaration = new(optional, mergeStrategyFieldPrefix, rebasePolicyFieldPrefix);
        _conversion = new(optional, cloneContext, referenceComparer, expressions);
        _merge = new(
            optional,
            mergeStrategyFieldPrefix,
            referenceComparer,
            expressions,
            fieldQualifier
        );
        _clone = new(optional, cloneContext, referenceComparer, expressions);
    }

    public static bool RequiresPortableSetView(
        bool bclHashSetImplementsReadOnlySet,
        IEnumerable<string?> namedTypeDefinitions
    ) =>
        SparseFragmentCollectionCloneEmitter.RequiresPortableSetView(
            bclHashSetImplementsReadOnlySet,
            namedTypeDefinitions
        );

    public string MergeStrategyField(SparseMemberModel member) =>
        _declaration.MergeStrategyField(member);

    public void AppendBuilder(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string accessibility = "public"
    ) => _declaration.AppendBuilder(code, members, accessibility);

    public static void AppendDeclaration(
        SharedIndentedBuilder code,
        string fragmentInterface,
        string deepCloneable,
        System.Action<SharedIndentedBuilder>? appendAttributes = null,
        string? advancedInterface = null,
        string accessibility = "public"
    ) =>
        SparseFragmentDeclarationEmitter.AppendDeclaration(
            code,
            fragmentInterface,
            deepCloneable,
            appendAttributes,
            advancedInterface,
            accessibility
        );

    public void AppendMembers(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string mergeStrategy,
        System.Action<SharedIndentedBuilder>? appendMemberAttributes = null,
        string? rebasePolicyBase = null,
        System.Func<SparseMemberModel, string>? rebasePolicyField = null
    ) =>
        _declaration.AppendMembers(
            code,
            members,
            mergeStrategy,
            appendMemberAttributes,
            rebasePolicyBase,
            rebasePolicyField
        );

    public static void AppendCollectionCloneHelpers(
        SharedIndentedBuilder code,
        bool includePortableSetView,
        bool hashSetSupportsCapacity
    ) =>
        SparseFragmentCollectionCloneEmitter.AppendCollectionCloneHelpers(
            code,
            includePortableSetView,
            hashSetSupportsCapacity
        );

    public void AppendDeepClone(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool usesPocoCloning,
        ModelConstructorBinding? constructor = null,
        bool modelIsReferenceType = true,
        string? operationsType = null,
        string receiver = "this."
    ) =>
        _clone.AppendDeepClone(
            code,
            modelType,
            members,
            usesPocoCloning,
            constructor,
            modelIsReferenceType,
            operationsType,
            receiver
        );

    public void AppendPocoCloneHelper(
        SharedIndentedBuilder code,
        string typeName,
        string cloneHelperName,
        ImmutableArray<SparseMemberModel> members,
        ModelConstructorBinding? constructor = null,
        string? operationsType = null,
        string helperAccessibility = "private"
    ) =>
        _clone.AppendPocoCloneHelper(
            code,
            typeName,
            cloneHelperName,
            members,
            constructor,
            operationsType,
            helperAccessibility
        );

    public void AppendFromModel(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType,
        bool usesPocoCloning,
        string? operationsType = null
    ) =>
        _conversion.AppendFromModel(
            code,
            modelType,
            members,
            modelIsReferenceType,
            usesPocoCloning,
            operationsType
        );

    public static void AppendRootProjectionConstructor(
        SharedIndentedBuilder code,
        string modelName,
        ImmutableArray<SparseMemberModel> members,
        ModelConstructorBinding? constructor = null,
        string bridgeAccessibility = "private"
    ) =>
        SparseFragmentConversionEmitter.AppendRootProjectionConstructor(
            code,
            modelName,
            members,
            constructor,
            bridgeAccessibility
        );

    public static void AppendToModel(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool hasRootProjectionConstructor = false,
        ModelConstructorBinding? constructor = null,
        string? operationsType = null,
        string receiver = ""
    ) =>
        SparseFragmentConversionEmitter.AppendToModel(
            code,
            modelType,
            members,
            hasRootProjectionConstructor,
            constructor,
            operationsType,
            receiver
        );

    public void AppendMerge(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string? operationsType = null,
        string receiver = "this."
    ) => _merge.AppendMerge(code, members, operationsType, receiver);

    public void AppendApplyChanges(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string? operationsType = null,
        string receiver = "this."
    ) => _merge.AppendApplyChanges(code, members, operationsType, receiver);

    public void AppendDiff(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType,
        string? operationsType = null
    ) => _merge.AppendDiff(code, modelType, members, modelIsReferenceType, operationsType);

    public void AppendFragmentClone(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        bool usesPocoCloning
    ) => _clone.AppendFragmentClone(code, members, usesPocoCloning);
}
