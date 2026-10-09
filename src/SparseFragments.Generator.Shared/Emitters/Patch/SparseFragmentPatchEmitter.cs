using System;
using System.Collections.Immutable;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits typed standalone mutations without source routing or ownership.</summary>
/// <remarks>
/// This type owns the shared patch vocabulary (dialect, naming, whole-operation
/// helpers) and orchestrates the patch stages. Member emission, patch algebra
/// and rebase each live in their own focused emitter.
/// </remarks>
internal static class SparseFragmentPatchEmitter
{
    internal static string Field(SparseMemberModel member) => "__sparse_patch_member_" + member.Id;

    internal static string ValueType(SparseMemberModel member) =>
        member.ChildModel is null ? member.Property.Type.Name : member.ChildFragmentType + "?";

    internal static string ChildPatch(SparseMemberModel member) =>
        SparseSemanticReference.ChildPatchType(
            member.ChildFragmentType!,
            SparseFamilyNames.Standalone
        );

    internal static bool IsCollectionPatch(SparseMemberModel member) =>
        SparseKeyedCollectionEmitter.IsCollectionPatch(member);

    internal static string CollectionPatch(SparseMemberModel member) =>
        SparseKeyedCollectionEmitter.CollectionPatchName(member);

    public static void AppendFragmentMethods(
        SharedIndentedBuilder code,
        string modelType,
        string runtimeNamespace,
        ImmutableArray<SparseMemberModel> members,
        bool canWriteInPlace,
        SparseWriteContract? writeContract = null,
        SparseEmissionFeatures? features = null
    )
    {
        var plan = features ?? SparseEmissionFeatures.Standalone;
        if (plan.EmitPatch)
        {
            code.AppendLineAt(2, "public Patch ToPatch() => new(this);");
            code.AppendLineAt(2, "public Fragment Apply(Patch patch)");
            code.AppendLineAt(2, "{");
            code.AppendLineAt(
                3,
                "if (patch is null) throw new global::System.ArgumentNullException(nameof(patch));"
            );
            code.AppendLineAt(
                3,
                "var result = patch.Apply("
                    + runtimeNamespace
                    + "Optional<Fragment?>.Present(this));"
            );
            code.AppendLineAt(
                3,
                "if (!result.IsPresent || result.Value is null) throw new global::System.InvalidOperationException(\"Apply a whole-contribution null or remove operation through Patch.Apply to preserve its optional state.\");"
            );
            code.AppendLineAt(3, "return result.Value;");
            code.AppendLineAt(2, "}");
        }
        if (canWriteInPlace)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Writes this fragment into an existing model instance.</summary>"
            );
            code.AppendLineAt(2, "public void WriteTo(" + modelType + " model)");
            code.AppendLineAt(2, "{");
            code.AppendLineAt(
                3,
                "if (model is null) throw new global::System.ArgumentNullException(nameof(model));"
            );
            code.AppendLineAt(3, "__SparseWriteWritableTo(model);");
            code.AppendLineAt(2, "}");
        }
        AppendWriteToContract(code, modelType, members, writeContract);
    }

    /// <summary>Writes this fragment into a separately owned write command.</summary>
    /// <remarks>
    /// The write model is never assumed to be the read model: only the
    /// contract's type and mapped member names are used. Assignability stays
    /// downstream and is checked by the consuming compilation.
    /// </remarks>
    internal static void AppendWriteToContract(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        SparseWriteContract? writeContract
    )
    {
        if (writeContract is null)
            return;
        if (string.Equals(writeContract.WriteModelType, modelType, StringComparison.Ordinal))
            return;
        code.AppendLineAt(
            2,
            "/// <summary>Writes this fragment into an existing write-command instance.</summary>"
        );
        code.AppendLineAt(2, "public void WriteTo(" + writeContract.WriteModelType + " model)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (model is null) throw new global::System.ArgumentNullException(nameof(model));"
        );
        code.AppendLineAt(3, "var __sparse_updated = ToModel();");
        foreach (var member in members)
        {
            var readProperty = SparseNaming.EscapeIdentifier(member.Property.Name);
            var writeProperty = SparseNaming.EscapeIdentifier(
                writeContract.GetWriteMemberName(member.Property.Name)
            );
            if (
                member.Collection.Kind == SparseCollectionKind.List
                && member.Collection.CloneKind == SparseCloneCollectionKind.List
            )
            {
                var listType =
                    "global::System.Collections.Generic.List<"
                    + member.Collection.ElementType.Name
                    + ">";
                code.AppendLineAt(
                    3,
                    "if (model."
                        + writeProperty
                        + " is "
                        + listType
                        + " __sparse_write_list"
                        + member.Id
                        + " && __sparse_updated."
                        + readProperty
                        + " is not null)"
                );
                code.AppendLineAt(3, "{");
                code.AppendLineAt(4, "__sparse_write_list" + member.Id + ".Clear();");
                code.AppendLineAt(
                    4,
                    "__sparse_write_list"
                        + member.Id
                        + ".AddRange(__sparse_updated."
                        + readProperty
                        + ");"
                );
                code.AppendLineAt(3, "}");
                code.AppendLineAt(
                    3,
                    "else model." + writeProperty + " = __sparse_updated." + readProperty + "!;"
                );
            }
            else if (member.Collection.IsDictionary)
            {
                var dictionaryType =
                    "global::System.Collections.Generic.Dictionary<"
                    + member.Collection.ElementType.Name
                    + ", "
                    + member.Collection.ValueType!.Value.Name
                    + ">";
                code.AppendLineAt(
                    3,
                    "if (model."
                        + writeProperty
                        + " is "
                        + dictionaryType
                        + " __sparse_write_dict"
                        + member.Id
                        + " && __sparse_updated."
                        + readProperty
                        + " is not null)"
                );
                code.AppendLineAt(3, "{");
                code.AppendLineAt(4, "__sparse_write_dict" + member.Id + ".Clear();");
                code.AppendLineAt(
                    4,
                    "foreach (var __sparse_write_pair"
                        + member.Id
                        + " in __sparse_updated."
                        + readProperty
                        + ") __sparse_write_dict"
                        + member.Id
                        + ".Add(__sparse_write_pair"
                        + member.Id
                        + ".Key, __sparse_write_pair"
                        + member.Id
                        + ".Value);"
                );
                code.AppendLineAt(3, "}");
                code.AppendLineAt(
                    3,
                    "else model." + writeProperty + " = __sparse_updated." + readProperty + "!;"
                );
            }
            else
            {
                code.AppendLineAt(
                    3,
                    "model." + writeProperty + " = __sparse_updated." + readProperty + "!;"
                );
            }
        }
        code.AppendLineAt(2, "}");
    }

    /// <summary>Small dialect for shared patch-core emission (whole, empty, ctor, apply).</summary>
    /// <remarks>
    /// Product generators configure member transport, rebase behavior and the
    /// write contract here; standalone defaults keep full disclosure with no
    /// write model. Anything product-specific stays in this configuration.
    /// </remarks>
    /// <param name="InPlaceWriteUnavailableKindMemberName">
    /// Conflict-kind member used for immutable in-place writes, or null to omit
    /// ChangeSet in-place APIs for models with immutable members.
    /// </param>
    /// <param name="PayloadImplementationContainerPrefix">
    /// Product-owned prefix for the nested payload implementation container.
    /// </param>
    internal readonly record struct SparsePatchDialect(
        string RuntimeNamespace,
        string WholeFieldName,
        string MembersEmptyName,
        Func<SparseMemberModel, string> MemberField,
        Func<SparseMemberModel, string> NestedContract,
        string NestedApplyMethod,
        bool CastNestedApply,
        string RuntimeFacade,
        string ConflictType,
        string ConflictKindType,
        Func<string, string> RebaseResult,
        Func<SparseMemberModel, string> ChildPatchName,
        Func<SparseMemberModel, string> ChildChangeSetName,
        string PayloadImplementationContainerPrefix,
        bool HashSetSupportsCapacity = false,
        bool HashSetImplementsReadOnlySet = false,
        Func<SparseMemberModel, string>? MemberValueType = null,
        Func<SparseMemberModel, string>? CollectionPatchName = null,
        Func<SparseMemberModel, string>? MergeStrategyField = null,
        string ChangePayloadVersion = "0.1",
        string? RebaseOptionsType = null,
        string? RebaseModeType = null,
        string? RebasePolicyType = null,
        Func<SparseMemberModel, string>? RebasePolicyField = null,
        ImmutableArray<SparseMemberPolicy> MemberPolicies = default,
        SparseRebasePolicy? RebasePolicy = null,
        SparseWriteContract? WriteContract = null,
        string? InPlaceWriteUnavailableKindMemberName = null
    )
    {
        /// <summary>Configured member policies, or empty for full disclosure.</summary>
        public ImmutableArray<SparseMemberPolicy> EffectiveMemberPolicies =>
            MemberPolicies.IsDefault ? ImmutableArray<SparseMemberPolicy>.Empty : MemberPolicies;

        /// <summary>Effective rebase behavior, defaulting to passthrough.</summary>
        public SparseRebasePolicy EffectiveRebasePolicy =>
            RebasePolicy ?? SparseRebasePolicy.Passthrough;

        /// <summary>Transport configured for one member name.</summary>
        public SparseMemberTransport GetTransport(string memberName) =>
            SparseDownstreamPolicy.GetTransport(EffectiveMemberPolicies, memberName);
    }

    internal static string DefaultChildChangeSet(SparseMemberModel member) =>
        SparseSemanticReference.ChildChangeSetType(
            member.ChildFragmentType!,
            SparseFamilyNames.Standalone
        );

    internal static string Operation(SparsePatchDialect dialect) =>
        dialect.RuntimeNamespace + "FragmentOperation";

    internal static string GetMemberValueType(
        SparsePatchDialect dialect,
        SparseMemberModel member
    ) => dialect.MemberValueType?.Invoke(member) ?? ValueType(member);

    internal static string GetCollectionPatchName(
        SparsePatchDialect dialect,
        SparseMemberModel member
    ) => dialect.CollectionPatchName?.Invoke(member) ?? CollectionPatch(member);

    internal static string GetMergeStrategyField(
        SparsePatchDialect dialect,
        SparseMemberModel member
    ) =>
        dialect.MergeStrategyField?.Invoke(member)
        ?? SparseWellKnownNames.MergeStrategyFieldPrefix + member.Id;

    /// <summary>Resolves the caller-owned per-rebase options type for generated signatures.</summary>
    /// <remarks>Null falls back to the runtime namespace so downstream products
    /// keep owning the type; Shared never substitutes a SparseFragments type.</remarks>
    internal static string GetRebaseOptionsType(SparsePatchDialect dialect) =>
        dialect.RebaseOptionsType ?? dialect.RuntimeNamespace + "ChangePayloadRebaseOptions";

    /// <summary>Resolves the caller-owned rebase mode enum for generated dispatch.</summary>
    internal static string GetRebaseModeType(SparsePatchDialect dialect) =>
        dialect.RebaseModeType ?? dialect.RuntimeNamespace + "SparseRebaseMode";

    /// <summary>Resolves the caller-owned rebase policy base type for generated fields.</summary>
    internal static string GetRebasePolicyType(SparsePatchDialect dialect) =>
        dialect.RebasePolicyType ?? dialect.RuntimeNamespace + "FragmentRebasePolicy";

    internal static string GetRebasePolicyField(
        SparsePatchDialect dialect,
        SparseMemberModel member
    ) =>
        dialect.RebasePolicyField?.Invoke(member)
        ?? SparseWellKnownNames.RebasePolicyFieldPrefix + member.Id;

    internal static string Kind(SparsePatchDialect dialect) =>
        dialect.RuntimeNamespace + "FragmentOperationKind";

    internal static string OptionalFragment(SparsePatchDialect dialect) =>
        dialect.RuntimeNamespace + "Optional<Fragment?>";

    internal static string MembersEmptyExpression(
        ImmutableArray<SparseMemberModel> members,
        SparsePatchDialect dialect
    )
    {
        if (members.IsEmpty)
            return "true";
        return string.Join(
            " && ",
            members.Select(member => MemberEmptyExpression(member, dialect))
        );
    }

    internal static string MemberEmptyExpression(
        SparseMemberModel member,
        SparsePatchDialect dialect
    )
    {
        if (member.ChildModel is not null || IsCollectionPatch(member))
        {
            return "("
                + dialect.MemberField(member)
                + " is null || "
                + dialect.MemberField(member)
                + ".__SparseIsEmpty())";
        }

        return dialect.MemberField(member) + ".Kind == " + Kind(dialect) + ".Keep";
    }

    public static void AppendPatch(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        SparsePatchDialect dialect,
        ImmutableArray<string> ignoredSettablePropertyNames = default,
        bool canApplyInPlace = false,
        SparseEmissionFeatures? features = null,
        string accessibility = "public",
        string? implementationNamespace = null,
        SharedIndentedBuilder? implementationBuilder = null
    )
    {
        var plan = features ?? SparseEmissionFeatures.Standalone;
        if (!plan.EmitPatch)
            return;
        var optional = dialect.RuntimeNamespace + "Optional<Fragment?>";
        code.AppendLineAt(
            1,
            "/// <summary>Desired-operation patch without baseline history. Applies directly to fragments and models.</summary>"
        );
        code.AppendLineAt(1, accessibility + " sealed class Patch");
        code.AppendLineAt(1, "{");
        SparseKeyedCollectionEmitter.EmitCollectionPatches(
            code,
            members,
            dialect,
            modelType,
            implementationNamespace
        );
        SparseFragmentPatchCoreEmitter.AppendPatchMembers(code, members, dialect);
        SparseFragmentPatchCoreEmitter.AppendPatchWholeOperations(
            code,
            modelType,
            string.Empty,
            string.Empty,
            members,
            dialect
        );
        SparseFragmentPatchCoreEmitter.AppendPatchConstructor(code, members, dialect);
        SparseFragmentPatchCoreEmitter.AppendPatchOptionalApply(
            code,
            members,
            dialect,
            "public " + optional + " Apply(" + optional + " current)"
        );
        SparseFragmentPatchCoreEmitter.AppendPatchApplyMembers(code, members, dialect);
        code.AppendLineAt(
            2,
            "/// <summary>Applies this patch to an ordinary model and returns a new model.</summary>"
        );
        code.AppendLineAt(2, "public " + modelType + " ApplyTo(" + modelType + " current)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var updated = Fragment.From(current).Apply(this).ToModel();");
        foreach (
            var ignoredName in ignoredSettablePropertyNames.IsDefault
                ? ImmutableArray<string>.Empty
                : ignoredSettablePropertyNames
        )
        {
            var name = SparseNaming.EscapeIdentifier(ignoredName);
            code.AppendLineAt(3, "updated." + name + " = current." + name + ";");
        }
        code.AppendLineAt(3, "return updated;");
        code.AppendLineAt(2, "}");
        if (canApplyInPlace)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Result of applying a patch to an existing model instance.</summary>"
            );
            code.AppendLineAt(2, "public sealed class ApplyInPlaceResult");
            code.AppendLineAt(2, "{");
            code.AppendLineAt(3, "private ApplyInPlaceResult(string[] unsupportedMembers)");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "UnsupportedMembers = global::System.Array.AsReadOnly(unsupportedMembers);"
            );
            code.AppendLineAt(3, "}");
            code.AppendLineAt(
                3,
                "/// <summary>Whether the patch was applied to the model.</summary>"
            );
            code.AppendLineAt(3, "public bool Succeeded => UnsupportedMembers.Count == 0;");
            code.AppendLineAt(
                3,
                "/// <summary>Names of immutable members that prevented the patch from being applied.</summary>"
            );
            code.AppendLineAt(
                3,
                "public global::System.Collections.Generic.IReadOnlyList<string> UnsupportedMembers { get; }"
            );
            code.AppendLineAt(
                3,
                "internal static ApplyInPlaceResult Success { get; } = new(global::System.Array.Empty<string>());"
            );
            code.AppendLineAt(
                3,
                "internal static ApplyInPlaceResult Failure(global::System.Collections.Generic.List<string> unsupportedMembers) => new(unsupportedMembers.ToArray());"
            );
            code.AppendLineAt(2, "}");
            code.AppendLineAt(
                2,
                "/// <summary>Applies this patch to writable members of an existing model.</summary>"
            );
            code.AppendLineAt(
                2,
                "public ApplyInPlaceResult ApplyInPlace(" + modelType + " current)"
            );
            code.AppendLineAt(2, "{");
            code.AppendLineAt(
                3,
                "if (current is null) throw new global::System.ArgumentNullException(nameof(current));"
            );
            var immutableMembers = members
                .Where(static member => member.Property.IsReadOnly || member.Property.IsInitOnly)
                .ToArray();
            if (immutableMembers.Length > 0)
            {
                code.AppendLineAt(
                    3,
                    "var __sparse_unsupportedMembers = new global::System.Collections.Generic.List<string>();"
                );
                foreach (var member in immutableMembers)
                {
                    var propertyName =
                        "nameof("
                        + modelType
                        + "."
                        + SparseNaming.EscapeIdentifier(member.Property.Name)
                        + ")";
                    code.AppendLineAt(
                        3,
                        "if (__sparse_whole.Kind != "
                            + Kind(dialect)
                            + ".Keep) __sparse_unsupportedMembers.Add("
                            + propertyName
                            + ");"
                    );
                    code.AppendLineAt(
                        3,
                        "if (!("
                            + MemberEmptyExpression(member, dialect)
                            + ") && !__sparse_unsupportedMembers.Contains("
                            + propertyName
                            + ")) __sparse_unsupportedMembers.Add("
                            + propertyName
                            + ");"
                    );
                }
                code.AppendLineAt(
                    3,
                    "if (__sparse_unsupportedMembers.Count > 0) return ApplyInPlaceResult.Failure(__sparse_unsupportedMembers);"
                );
            }
            code.AppendLineAt(
                3,
                "Fragment.From(current).Apply(this).__SparseWriteWritableTo(current);"
            );
            code.AppendLineAt(3, "return ApplyInPlaceResult.Success;");
            code.AppendLineAt(2, "}");
        }
        SparseFragmentPatchAlgebraEmitter.AppendPatchAlgebra(code, modelType, members, dialect);
        SparseFragmentPatchRebaseEmitter.AppendPatchRebase(code, modelType, members, dialect);
        if (plan.EmitChangePayload)
        {
            SparseChangePayloadPatchSyncEmitter.AppendPatchToPayloadCore(
                code,
                members,
                dialect,
                modelType,
                implementationNamespace
            );
        }
        code.AppendLineAt(1, "}");
        if (plan.EmitChangeSet)
        {
            var canApplyChangeSetInPlace =
                canApplyInPlace
                && (
                    dialect.InPlaceWriteUnavailableKindMemberName is not null
                    || members.All(static member =>
                        !member.Property.IsReadOnly && !member.Property.IsInitOnly
                    )
                );
            SparseChangeSetEmitter.AppendChangeSet(
                code,
                members,
                dialect,
                modelType,
                ignoredSettablePropertyNames,
                plan,
                canApplyChangeSetInPlace,
                accessibility,
                implementationNamespace,
                implementationBuilder
            );
        }
    }
}
