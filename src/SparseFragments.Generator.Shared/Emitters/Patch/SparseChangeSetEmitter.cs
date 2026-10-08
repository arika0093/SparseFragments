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
        ImmutableArray<SparseMemberModel> members
    ) => AppendChangeSet(code, members, SparseFragmentPatchEmitter.StandaloneDialect(), null);

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
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var optionalFragment = runtime + "Optional<Fragment?>";
        var rebaseResult = dialect.RebaseResult("ChangeSet");
        var prefix = SparseNaming.PatchApiPrefix(
            members.Select(static member => member.Property.Name)
        );
        var between = "Patch." + prefix + "Between";
        var rebase = "Patch." + prefix + "Rebase";
        SparsePatchStjEmitter.AppendChangeSetConverterAttribute(code);
        code.AppendLineAt(1, "public sealed class ChangeSet");
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
            dialect
        );
        if (modelType is not null)
        {
            SparseChangeSetBetweenEmitter.AppendModelBetween(code, modelType, optionalFragment);
        }
        SparseChangeSetPatchSyncEmitter.AppendFromPatch(
            code,
            members,
            optionalFragment,
            modelType
        );
        SparseChangeSetPatchSyncEmitter.AppendToPatch(code, members, runtime, prefix, dialect);
        SparseChangeSetPatchSyncEmitter.AppendInvert(code, members, runtime, optionalFragment);
        SparseChangeSetComposeEmitter.AppendCompose(
            code,
            members,
            runtime,
            optionalFragment,
            dialect
        );
        SparseChangeSetMatchEmitter.AppendMatchHelpers(
            code,
            members,
            runtime,
            optionalFragment,
            dialect
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
            dialect
        );
        SparseChangeSetTransitionEmitter.AppendTypedSurface(code, members, dialect);
        SparsePatchStjEmitter.AppendChangeSetStj(code, members, dialect);
        code.AppendLineAt(1, "}");
    }

    internal static void ComputePublicNames(
        ImmutableArray<SparseMemberModel> members,
        out Dictionary<int, string> propNames,
        out Dictionary<int, string> transNames
    )
    {
        var reserved = new HashSet<string>(System.StringComparer.Ordinal)
        {
            "IsEmpty",
            "Between",
            "FromPatch",
            "ToPatch",
            "Invert",
            "Compose",
            "RebaseOnto",
        };
        var usedProps = new HashSet<string>(reserved, System.StringComparer.Ordinal);
        propNames = new Dictionary<int, string>();
        foreach (var member in members)
        {
            var prefix = new System.Text.StringBuilder();
            while (usedProps.Contains(prefix.ToString() + member.Property.Name))
                prefix.Append("Sparse");
            var candidate = prefix.ToString() + member.Property.Name;
            usedProps.Add(candidate);
            propNames[member.Id] = candidate;
        }
        var usedTypes = new HashSet<string>(usedProps, System.StringComparer.Ordinal);
        transNames = new Dictionary<int, string>();
        foreach (var member in members)
        {
            if (SparseChangeSetBasicsEmitter.IsNested(member))
                continue;
            var prefix = new System.Text.StringBuilder();
            while (usedTypes.Contains(prefix.ToString() + propNames[member.Id] + "Transition"))
                prefix.Append("Sparse");
            var t = prefix.ToString() + propNames[member.Id] + "Transition";
            usedTypes.Add(t);
            transNames[member.Id] = t;
        }
    }
}
