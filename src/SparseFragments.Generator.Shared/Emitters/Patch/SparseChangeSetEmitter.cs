using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits the immutable baseline-aware ChangeSet sibling for a generated model.</summary>
/// <remarks>Canonical storage is sparse per-path transition state; Compose/Rebase operate directly on it. This facade preserves the public API and delegates to single-responsibility emitters.</remarks>
internal static class SparseChangeSetEmitter
{
    public static void AppendChangeSet(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    ) => AppendChangeSet(code, members, dialect, null);

    public static void AppendChangeSet(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType
    ) => AppendChangeSet(code, members, dialect, modelType, default);

    public static void AppendChangeSet(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        ImmutableArray<string> ignoredSettablePropertyNames,
        SparseEmissionFeatures? features = null,
        bool canApplyInPlace = false,
        string accessibility = "public",
        SparseOperationTarget? target = null
    )
    {
        var plan = features ?? SparseEmissionFeatures.Standalone;
        var runtime = dialect.RuntimeNamespace;
        var optionalFragment = runtime + "Optional<Fragment?>";
        var rebaseResult = dialect.RebaseResult("ChangeSet");
        var prefix = SparseNaming.PatchApiPrefix(
            members.Select(static member => member.Property.Name)
        );
        var between = "Patch." + prefix + "Between";
        var rebase = "Patch." + prefix + "Rebase";
        code.AppendLineAt(
            1,
            "/// <summary>Baseline-aware transition between two states. Validates before-states and supports compose and rebase.</summary>"
        );
        code.AppendLineAt(1, accessibility + " sealed class ChangeSet");
        code.AppendLineAt(1, "{");
        SparseChangeSetBasicsEmitter.AppendFields(
            code,
            members,
            runtime,
            optionalFragment,
            dialect
        );
        SparseChangeSetBasicsEmitter.AppendConstructor(
            code,
            members,
            runtime,
            optionalFragment,
            dialect
        );
        SparseChangeSetBasicsEmitter.AppendIsEmpty(code, members);
        SparseChangeSetBetweenEmitter.AppendBetween(
            code,
            members,
            runtime,
            optionalFragment,
            dialect,
            target
        );
        if (modelType is not null)
        {
            SparseChangeSetBetweenEmitter.AppendModelBetween(
                code,
                modelType,
                optionalFragment,
                target
            );
        }
        SparseChangeSetPatchSyncEmitter.AppendFromPatch(
            code,
            members,
            optionalFragment,
            modelType,
            target
        );
        SparseChangeSetPatchSyncEmitter.AppendToPatch(
            code,
            members,
            runtime,
            prefix,
            dialect,
            target
        );
        SparseChangeSetPatchSyncEmitter.AppendInvert(
            code,
            members,
            runtime,
            optionalFragment,
            target
        );
        SparseChangeSetPatchSyncEmitter.AppendApplyToBaseline(code, optionalFragment, target);
        SparseChangeSetComposeEmitter.AppendCompose(
            code,
            members,
            runtime,
            optionalFragment,
            dialect,
            target
        );
        SparseChangeSetMatchEmitter.AppendMatchHelpers(
            code,
            members,
            runtime,
            optionalFragment,
            dialect,
            target
        );
        SparseChangeSetRebaseEmitter.AppendRebase(
            code,
            members,
            runtime,
            optionalFragment,
            rebaseResult,
            prefix,
            rebase,
            between,
            dialect,
            modelType,
            ignoredSettablePropertyNames,
            canApplyInPlace,
            target
        );
        // The emitted model-targeted ApplyInPlace checks the before-state first.
        SparseChangeSetTransitionEmitter.AppendTypedSurface(code, members, dialect, target);
        SparseChangeSetEnumeratorEmitter.Append(code, members, dialect, target);
        SparseChangeSetPathEmitter.Append(code, members, target);
        if (plan.EmitChangePayload)
        {
            SparseChangeSetPayloadEmitter.AppendToPayload(
                code,
                members,
                dialect,
                modelType,
                target
            );
            SparseChangePayloadReaderEmitter.AppendFromPayload(
                code,
                members,
                dialect,
                modelType,
                target
            );
            SparseChangePayloadPatchSyncEmitter.AppendPatchFromCore(
                code,
                members,
                dialect,
                modelType,
                target
            );
            SparseChangeSetMixedEmitter.AppendMixedPartition(
                code,
                members,
                dialect,
                modelType,
                target
            );
        }
        code.AppendLineAt(1, "}");
        code.AppendLine();
        if (plan.EmitChangePayload)
        {
            SparseChangeSetPayloadEmitter.AppendPayload(
                code,
                members,
                dialect,
                modelType,
                ignoredSettablePropertyNames
            );
        }
    }

    internal static void ComputePublicNames(
        ImmutableArray<SparseMemberModel> members,
        out Dictionary<int, string> propNames,
        out Dictionary<int, string> transNames
    ) => SparseChangeSetNaming.ComputePublicNames(members, out propNames, out transNames);
}
