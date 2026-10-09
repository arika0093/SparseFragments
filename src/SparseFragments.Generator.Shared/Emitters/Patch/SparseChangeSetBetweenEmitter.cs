using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static SparseFragments.Generator.Shared.SparseChangeSetBasicsEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetComposeEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictBetweenEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictComposeEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictTransitionEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedBetweenEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedComposeEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedTransitionEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetMatchEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetPatchSyncEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetTransitionEmitter;

namespace SparseFragments.Generator.Shared;

/// <summary>Baseline-aware Between diff dispatch.</summary>
internal static class SparseChangeSetBetweenEmitter
{
    internal static void AppendBetween(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null
    )
    {
        if (target is not null)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Derives the canonical baseline-aware diff between two states.</summary>"
            );
            code.AppendLineAt(
                2,
                "/// <remarks>The change set snapshots its inputs: whole-root fragments are deep-cloned and member collection containers are copied at capture, so later caller-side mutation cannot alter retained history. Element values are shared by reference. Typed <c>Before</c>/<c>After</c> endpoints return fresh container snapshots for collection members.</remarks>"
            );
            code.AppendLineAt(
                2,
                "public static ChangeSet Between("
                    + optionalFragment
                    + " before, "
                    + optionalFragment
                    + " after) => "
                    + target.ChangeSetOperationsType
                    + ".Between(before, after);"
            );
            // The algorithm body below streams into the operation container.
            code = target.ChangeSetOperations;
        }
        code.AppendLineAt(2, "/// <summary>Deep-clones a retained whole-root fragment.</summary>");
        code.AppendLineAt(
            2,
            "private static "
                + optionalFragment
                + " __SparseCloneRoot("
                + optionalFragment
                + " value) => !value.IsPresent || value.Value is null ? value : "
                + optionalFragment
                + ".Present(value.Value!.DeepClone());"
        );
        code.AppendLineAt(
            2,
            "/// <summary>Derives the canonical baseline-aware diff between two states.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>The change set snapshots its inputs: whole-root fragments are deep-cloned and member collection containers are copied at capture, so later caller-side mutation cannot alter retained history. Element values are shared by reference. Typed <c>Before</c>/<c>After</c> endpoints return fresh container snapshots for collection members.</remarks>"
        );
        code.AppendLineAt(
            2,
            (
                target is null
                    ? "public static ChangeSet Between("
                    : "internal static ChangeSet Between("
            )
                + optionalFragment
                + " before, "
                + optionalFragment
                + " after)"
        );
        code.AppendLineAt(2, "{");
        var empty = EmptyArgs(members);
        code.AppendLineAt(3, "if (before.IsPresent != after.IsPresent)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "return new ChangeSet(true, __SparseCloneRoot(before), __SparseCloneRoot(after), "
                + MemberEmptyTail(members)
                + ");"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "if (!before.IsPresent) return new ChangeSet(" + empty + ");");
        code.AppendLineAt(
            3,
            "if (global::System.Object.ReferenceEquals(before.Value, after.Value)) return new ChangeSet("
                + empty
                + ");"
        );
        code.AppendLineAt(3, "if (before.Value is null || after.Value is null)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (before.Value is null && after.Value is null) return new ChangeSet(" + empty + ");"
        );
        code.AppendLineAt(
            4,
            "return new ChangeSet(true, __SparseCloneRoot(before), __SparseCloneRoot(after), "
                + MemberEmptyTail(members)
                + ");"
        );
        code.AppendLineAt(3, "}");
        if (members.IsDefaultOrEmpty || members.Length == 0)
        {
            code.AppendLineAt(3, "return new ChangeSet(" + empty + ");");
            code.AppendLineAt(2, "}");
            return;
        }
        code.AppendLineAt(3, "var bf = before.Value!;");
        code.AppendLineAt(3, "var af = after.Value!;");
        foreach (var member in members)
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            if (IsNested(member))
            {
                var child = ChildChangeSet(member, dialect);
                code.AppendLineAt(
                    3,
                    "var __n"
                        + member.Id
                        + " = "
                        + child
                        + ".Between(bf."
                        + name
                        + ", af."
                        + name
                        + ");"
                );
                code.AppendLineAt(
                    3,
                    child
                        + "? __nn"
                        + member.Id
                        + " = __n"
                        + member.Id
                        + ".IsEmpty ? null : __n"
                        + member.Id
                        + ";"
                );
            }
            else if (IsKeyed(member))
            {
                AppendKeyedBetweenSparse(code, member, name, runtime, dialect);
            }
            else if (IsDict(member))
            {
                AppendDictBetweenSparse(code, member, name, runtime, dialect);
            }
            else
            {
                var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
                var beforeAccess = "bf." + name;
                var afterAccess = "af." + name;
                if (SparseChangeSetTransitionEmitter.NeedsSnapshot(member))
                {
                    beforeAccess = "__SparseSnapshot_" + member.Id + "(" + beforeAccess + ")";
                    afterAccess = "__SparseSnapshot_" + member.Id + "(" + afterAccess + ")";
                }
                code.AppendLineAt(
                    3,
                    "bool __h"
                        + member.Id
                        + " = !Fragment.__SparseEqual_"
                        + member.Id
                        + "(bf."
                        + name
                        + ", af."
                        + name
                        + ");"
                );
                code.AppendLineAt(
                    3,
                    opt
                        + " __b"
                        + member.Id
                        + " = __h"
                        + member.Id
                        + " ? "
                        + beforeAccess
                        + " : default;"
                );
                code.AppendLineAt(
                    3,
                    opt
                        + " __a"
                        + member.Id
                        + " = __h"
                        + member.Id
                        + " ? "
                        + afterAccess
                        + " : default;"
                );
            }
        }
        // Empty fast path.
        var conds = new List<string>();
        foreach (var member in members)
        {
            if (IsNested(member))
                conds.Add("__nn" + member.Id + " is null");
            else
                conds.Add("!__h" + member.Id);
        }
        code.AppendLineAt(
            3,
            "if (" + string.Join(" && ", conds) + ") return new ChangeSet(" + empty + ");"
        );
        var args = new List<string> { "false", "default", "default" };
        foreach (var member in members)
        {
            if (IsNested(member))
                args.Add("__nn" + member.Id);
            else if (IsKeyed(member))
                args.AddRange(
                    new[]
                    {
                        "__h" + member.Id,
                        "__whole" + member.Id,
                        "__wb" + member.Id,
                        "__wa" + member.Id,
                        "__items" + member.Id,
                        "__bO" + member.Id,
                        "__aO" + member.Id,
                    }
                );
            else if (IsDict(member))
                args.AddRange(
                    new[]
                    {
                        "__h" + member.Id,
                        "__whole" + member.Id,
                        "__wb" + member.Id,
                        "__wa" + member.Id,
                        "__items" + member.Id,
                    }
                );
            else
                args.AddRange(new[] { "__b" + member.Id, "__a" + member.Id, "__h" + member.Id });
        }
        code.AppendLineAt(3, "return new ChangeSet(" + string.Join(", ", args) + ");");
        code.AppendLineAt(2, "}");
    }

    internal static void AppendModelBetween(
        SharedIndentedBuilder code,
        string modelType,
        string optionalFragment,
        SparseOperationTarget? target = null
    )
    {
        if (target is not null)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Derives the baseline-aware diff between two ordinary models.</summary>"
            );
            code.AppendLineAt(
                2,
                "public static ChangeSet Between("
                    + modelType
                    + " before, "
                    + modelType
                    + " after) => "
                    + target.ChangeSetOperationsType
                    + ".Between(before, after);"
            );
            code = target.ChangeSetOperations;
            code.AppendLineAt(
                2,
                "/// <summary>Derives the baseline-aware diff between two ordinary models.</summary>"
            );
            code.AppendLineAt(
                2,
                "internal static ChangeSet Between("
                    + modelType
                    + " before, "
                    + modelType
                    + " after)"
            );
        }
        else
        {
            code.AppendLineAt(
                2,
                "/// <summary>Derives the baseline-aware diff between two ordinary models.</summary>"
            );
            code.AppendLineAt(
                2,
                "public static ChangeSet Between(" + modelType + " before, " + modelType + " after)"
            );
        }
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "return Between("
                + optionalFragment
                + ".Present(Fragment.From(before)), "
                + optionalFragment
                + ".Present(Fragment.From(after)));"
        );
        code.AppendLineAt(2, "}");
    }
}
