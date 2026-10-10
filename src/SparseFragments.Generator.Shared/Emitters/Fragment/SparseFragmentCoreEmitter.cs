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

    public void AppendMemberCaches(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string mergeStrategy,
        string? rebasePolicyBase = null,
        System.Func<SparseMemberModel, string>? rebasePolicyField = null
    ) =>
        _declaration.AppendMemberCaches(
            code,
            members,
            mergeStrategy,
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

    public static void AppendDeepClonePublicFacade(
        SharedIndentedBuilder code,
        string modelType,
        string operationsType
    ) => SparseFragmentCloneEmitter.AppendDeepClonePublicFacade(code, modelType, operationsType);

    public static void AppendDeepCloneInternalFacade(
        SharedIndentedBuilder code,
        string modelType,
        string operationsType
    ) => SparseFragmentCloneEmitter.AppendDeepCloneInternalFacade(code, modelType, operationsType);

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

    public static void AppendFromModelInternalFacade(
        SharedIndentedBuilder code,
        string modelType,
        string operationsType
    ) =>
        SparseFragmentConversionEmitter.AppendFromModelInternalFacade(
            code,
            modelType,
            operationsType
        );

    public void AppendFromModelPublicBody(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType,
        bool requiresContext
    ) =>
        _conversion.AppendFromModelPublicBody(
            code,
            modelType,
            members,
            modelIsReferenceType,
            requiresContext
        );

    public void AppendFromModelInternalBody(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType
    ) => _conversion.AppendFromModelInternalBody(code, modelType, members, modelIsReferenceType);

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

    public static void AppendDiffInternalFacade(
        SharedIndentedBuilder code,
        string modelType,
        string operationsType
    ) => SparseFragmentMergeEmitter.AppendDiffInternalFacade(code, modelType, operationsType);

    public void AppendDiffPublicBody(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType
    ) => _merge.AppendDiffPublicBody(code, modelType, members, modelIsReferenceType);

    public static void AppendDiffInternalBody(
        SharedIndentedBuilder code,
        string modelType,
        bool modelIsReferenceType
    ) => SparseFragmentMergeEmitter.AppendDiffInternalBody(code, modelType, modelIsReferenceType);

    public void AppendDiffPrivateBodies(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType
    ) => _merge.AppendDiffPrivateBodies(code, modelType, members, modelIsReferenceType);

    public void AppendDeepCloneOperationsPublic(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        ModelConstructorBinding? constructor = null,
        bool modelIsReferenceType = true
    ) =>
        _clone.AppendDeepCloneOperationsPublic(
            code,
            modelType,
            members,
            constructor,
            modelIsReferenceType
        );

    public void AppendDeepCloneOperationsInternal(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        ModelConstructorBinding? constructor = null,
        bool modelIsReferenceType = true
    ) =>
        _clone.AppendDeepCloneOperationsInternal(
            code,
            modelType,
            members,
            constructor,
            modelIsReferenceType
        );

    public void AppendFragmentClone(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        bool usesPocoCloning
    ) => _clone.AppendFragmentClone(code, members, usesPocoCloning);

    public void AppendFragmentClonePublic(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        bool usesPocoCloning
    ) => _clone.AppendFragmentClonePublic(code, members, usesPocoCloning);

    public void AppendFragmentCloneInternal(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members
    ) => _clone.AppendFragmentCloneInternal(code, members);

    public void AppendFragmentCloneInternalMethod(SharedIndentedBuilder code) =>
        _clone.AppendFragmentCloneInternalMethod(code);

    public void AppendFragmentClonePrivateCtor(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members
    ) => _clone.AppendFragmentClonePrivateCtor(code, members);
}
