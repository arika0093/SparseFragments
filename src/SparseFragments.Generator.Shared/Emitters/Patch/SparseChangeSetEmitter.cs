using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits the immutable baseline-aware ChangeSet sibling for a generated model (issue #85, #96).</summary>
/// <remarks>
/// Issue #96: canonical storage is sparse per-path transition state, never full
/// before/after snapshots. Unchanged members retain nothing (Missing + has=false
/// or null nested). Between/FromPatch normalize to this sparse form; ToPatch/Invert
/// project from it; typed surface reads it; STJ serializes only changed paths.
/// Compose/Rebase are sparse-aware for memberwise transitions; whole-root and
/// keyed/dict member-level whole cases retain the member values they need.
/// Full sparse keyed algebra (granular-only retention, cross-member merge without
/// member values) is deferred to #97: keyed/dict changed members currently retain
/// their member-level before/after values (still sparse at member granularity).
/// </remarks>
internal static class SparseChangeSetEmitter
{
    public static void AppendChangeSet(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members
    )
    {
        var runtime = SparseFragmentPatchEmitter.Runtime;
        var optionalFragment = runtime + "Optional<Fragment?>";
        var rebaseResult = "global::SparseFragments.RebaseResult<ChangeSet>";
        var prefix = SparseNaming.PatchApiPrefix(
            members.Select(static member => member.Property.Name)
        );
        var between = "Patch." + prefix + "Between";
        var rebase = "Patch." + prefix + "Rebase";

        SparsePatchStjEmitter.AppendChangeSetConverterAttribute(code);
        code.AppendLineAt(1, "public sealed class ChangeSet");
        code.AppendLineAt(1, "{");
        AppendFields(code, members, runtime, optionalFragment);
        AppendConstructor(code, members, runtime, optionalFragment);
        AppendIsEmpty(code, members);
        AppendBetween(code, members, runtime, optionalFragment);
        AppendFromPatch(code, members, runtime, optionalFragment);
        AppendToPatch(code, members, runtime, prefix);
        AppendInvert(code, members, runtime, optionalFragment);
        AppendCompose(code, members, runtime, optionalFragment);
        AppendRebase(
            code,
            members,
            runtime,
            optionalFragment,
            rebaseResult,
            prefix,
            rebase,
            between
        );
        AppendTypedSurface(code, members);
        SparsePatchStjEmitter.AppendChangeSetStj(code, members);
        code.AppendLineAt(1, "}");
    }

    private static bool IsScalar(SparseMemberModel m) =>
        m.ChildModel is null && !SparseFragmentPatchEmitter.IsCollectionPatch(m);

    private static bool IsNested(SparseMemberModel m) =>
        m.ChildModel is not null && !SparseFragmentPatchEmitter.IsCollectionPatch(m);

    private static bool IsKeyed(SparseMemberModel m) =>
        SparseKeyedCollectionEmitter.IsKeyedSequence(m);

    private static bool IsDict(SparseMemberModel m) => SparseKeyedCollectionEmitter.IsDictionary(m);

    private static string FragmentValueType(SparseMemberModel m) =>
        SparseFragmentEmitHelpers.FragmentValueType(m);

    private static string ChildChangeSet(SparseMemberModel m) =>
        m.ChildFragmentType!.Substring(0, m.ChildFragmentType.Length - "Fragment".Length)
        + "ChangeSet";

    private static string BeforeField(SparseMemberModel m) => "__sparse_before_" + m.Id;

    private static string AfterField(SparseMemberModel m) => "__sparse_after_" + m.Id;

    private static string HasField(SparseMemberModel m) => "__sparse_has_" + m.Id;

    private static string NestedField(SparseMemberModel m) => "__sparse_nested_" + m.Id;

    private static void AppendFields(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment
    )
    {
        code.AppendLineAt(2, "private readonly bool __sparse_hasWhole;");
        code.AppendLineAt(2, "private readonly " + optionalFragment + " __sparse_wholeBefore;");
        code.AppendLineAt(2, "private readonly " + optionalFragment + " __sparse_wholeAfter;");
        foreach (var member in members)
        {
            if (IsNested(member))
            {
                code.AppendLineAt(
                    2,
                    "private readonly " + ChildChangeSet(member) + "? " + NestedField(member) + ";"
                );
            }
            else
            {
                var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
                code.AppendLineAt(2, "private readonly " + opt + " " + BeforeField(member) + ";");
                code.AppendLineAt(2, "private readonly " + opt + " " + AfterField(member) + ";");
                code.AppendLineAt(2, "private readonly bool " + HasField(member) + ";");
            }
        }
    }

    private static void AppendConstructor(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment
    )
    {
        var parts = new List<string>
        {
            "bool hasWhole",
            optionalFragment + " wholeBefore",
            optionalFragment + " wholeAfter",
        };
        foreach (var member in members)
        {
            if (IsNested(member))
            {
                parts.Add(ChildChangeSet(member) + "? nested" + member.Id);
            }
            else
            {
                var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
                parts.Add(opt + " before" + member.Id);
                parts.Add(opt + " after" + member.Id);
                parts.Add("bool has" + member.Id);
            }
        }
        code.AppendLineAt(2, "private ChangeSet(" + string.Join(", ", parts) + ")");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "__sparse_hasWhole = hasWhole;");
        code.AppendLineAt(3, "__sparse_wholeBefore = wholeBefore;");
        code.AppendLineAt(3, "__sparse_wholeAfter = wholeAfter;");
        foreach (var member in members)
        {
            if (IsNested(member))
            {
                code.AppendLineAt(3, NestedField(member) + " = nested" + member.Id + ";");
            }
            else
            {
                code.AppendLineAt(3, BeforeField(member) + " = before" + member.Id + ";");
                code.AppendLineAt(3, AfterField(member) + " = after" + member.Id + ";");
                code.AppendLineAt(3, HasField(member) + " = has" + member.Id + ";");
            }
        }
        code.AppendLineAt(2, "}");
    }

    private static string EmptyArgs(ImmutableArray<SparseMemberModel> members)
    {
        var parts = new List<string> { "false", "default", "default" };
        foreach (var member in members)
        {
            if (IsNested(member))
                parts.Add("null");
            else
                parts.AddRange(new[] { "default", "default", "false" });
        }
        return string.Join(", ", parts);
    }

    private static void AppendIsEmpty(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members
    )
    {
        code.AppendLineAt(
            2,
            "/// <summary>Whether this change set contains no semantic changes.</summary>"
        );
        var expr = new List<string> { "!__sparse_hasWhole" };
        foreach (var member in members)
        {
            if (IsNested(member))
                expr.Add(
                    "(" + NestedField(member) + " is null || " + NestedField(member) + ".IsEmpty)"
                );
            else
                expr.Add("!" + HasField(member));
        }
        code.AppendLineAt(2, "public bool IsEmpty => " + string.Join(" && ", expr) + ";");
    }

    private static void AppendBetween(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment
    )
    {
        code.AppendLineAt(
            2,
            "/// <summary>Derives the canonical baseline-aware diff between two states.</summary>"
        );
        code.AppendLineAt(
            2,
            "public static ChangeSet Between("
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
            "return new ChangeSet(true, before, after, " + MemberEmptyTail(members) + ");"
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
            "return new ChangeSet(true, before, after, " + MemberEmptyTail(members) + ");"
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
                var child = ChildChangeSet(member);
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
            else if (IsKeyed(member) || IsDict(member))
            {
                // Keyed/dict equality is granular (element-wise fragment aware):
                // member-level sequence/dictionary comparison alone cannot see
                // through cloned element instances, so reuse the canonical
                // collection diff exactly like Patch.Between does.
                var coll = "Patch." + SparseFragmentPatchEmitter.CollectionPatch(member);
                var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
                code.AppendLineAt(
                    3,
                    "bool __h"
                        + member.Id
                        + " = !"
                        + coll
                        + ".Between(bf."
                        + name
                        + ", af."
                        + name
                        + ").__SparseIsEmpty();"
                );
                code.AppendLineAt(
                    3,
                    opt
                        + " __b"
                        + member.Id
                        + " = __h"
                        + member.Id
                        + " ? bf."
                        + name
                        + " : default;"
                );
                code.AppendLineAt(
                    3,
                    opt
                        + " __a"
                        + member.Id
                        + " = __h"
                        + member.Id
                        + " ? af."
                        + name
                        + " : default;"
                );
            }
            else
            {
                var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
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
                        + " ? bf."
                        + name
                        + " : default;"
                );
                code.AppendLineAt(
                    3,
                    opt
                        + " __a"
                        + member.Id
                        + " = __h"
                        + member.Id
                        + " ? af."
                        + name
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
            else
                args.AddRange(new[] { "__b" + member.Id, "__a" + member.Id, "__h" + member.Id });
        }
        code.AppendLineAt(3, "return new ChangeSet(" + string.Join(", ", args) + ");");
        code.AppendLineAt(2, "}");
    }

    private static string MemberEmptyTail(ImmutableArray<SparseMemberModel> members)
    {
        if (members.IsDefaultOrEmpty || members.Length == 0)
            return string.Empty.Trim();
        var parts = new List<string>();
        foreach (var member in members)
        {
            if (IsNested(member))
                parts.Add("null");
            else
                parts.AddRange(new[] { "default", "default", "false" });
        }
        return string.Join(", ", parts);
    }

    private static void AppendFromPatch(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment
    )
    {
        _ = members;
        _ = runtime;
        code.AppendLineAt(
            2,
            "/// <summary>Attaches a known baseline to an arbitrary patch.</summary>"
        );
        code.AppendLineAt(
            2,
            "public static ChangeSet FromPatch(" + optionalFragment + " baseline, Patch patch)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (patch is null) throw new global::System.ArgumentNullException(nameof(patch));"
        );
        code.AppendLineAt(3, "var after = patch.Apply(baseline);");
        code.AppendLineAt(3, "return Between(baseline, after);");
        code.AppendLineAt(2, "}");
    }

    private static void AppendToPatch(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string prefix
    )
    {
        var between = "Patch." + prefix + "Between";
        code.AppendLineAt(
            2,
            "/// <summary>Discards baseline information and returns the equivalent desired-operation patch.</summary>"
        );
        code.AppendLineAt(2, "public Patch ToPatch()");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (__sparse_hasWhole) return "
                + between
                + "(__sparse_wholeBefore, __sparse_wholeAfter);"
        );
        code.AppendLineAt(3, "var patch = new Patch();");
        foreach (var member in members)
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            if (IsNested(member))
            {
                code.AppendLineAt(3, "if (" + NestedField(member) + " is not null)");
                code.AppendLineAt(3, "{");
                code.AppendLineAt(4, "patch." + name + " = " + NestedField(member) + ".ToPatch();");
                code.AppendLineAt(3, "}");
            }
            else if (IsKeyed(member) || IsDict(member))
            {
                var coll = "Patch." + SparseFragmentPatchEmitter.CollectionPatch(member);
                code.AppendLineAt(3, "if (" + HasField(member) + ")");
                code.AppendLineAt(3, "{");
                code.AppendLineAt(
                    4,
                    "patch."
                        + name
                        + " = "
                        + coll
                        + ".Between("
                        + BeforeField(member)
                        + ", "
                        + AfterField(member)
                        + ");"
                );
                code.AppendLineAt(3, "}");
            }
            else
            {
                var vt = SparseFragmentPatchEmitter.ValueType(member);
                var op = runtime + "FragmentOperation<" + vt + ">";
                code.AppendLineAt(3, "if (" + HasField(member) + ")");
                code.AppendLineAt(3, "{");
                code.AppendLineAt(
                    4,
                    "patch."
                        + name
                        + " = "
                        + AfterField(member)
                        + ".IsPresent ? "
                        + op
                        + ".Set("
                        + AfterField(member)
                        + ".Value) : "
                        + op
                        + ".Unset;"
                );
                code.AppendLineAt(3, "}");
            }
        }
        code.AppendLineAt(3, "return patch;");
        code.AppendLineAt(2, "}");
    }

    private static void AppendInvert(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment
    )
    {
        _ = runtime;
        _ = optionalFragment;
        code.AppendLineAt(
            2,
            "/// <summary>Swaps the transition direction without requiring a separate baseline.</summary>"
        );
        code.AppendLineAt(2, "public ChangeSet Invert()");
        code.AppendLineAt(2, "{");
        var emptyTail = MemberEmptyTail(members);
        code.AppendLineAt(3, "if (__sparse_hasWhole)");
        code.AppendLineAt(3, "{");
        if (string.IsNullOrEmpty(emptyTail))
            code.AppendLineAt(
                4,
                "return new ChangeSet(true, __sparse_wholeAfter, __sparse_wholeBefore);"
            );
        else
            code.AppendLineAt(
                4,
                "return new ChangeSet(true, __sparse_wholeAfter, __sparse_wholeBefore, "
                    + emptyTail
                    + ");"
            );
        code.AppendLineAt(3, "}");
        var args = new List<string> { "false", "default", "default" };
        foreach (var member in members)
        {
            if (IsNested(member))
                args.Add(
                    NestedField(member) + " is null ? null : " + NestedField(member) + ".Invert()"
                );
            else
                args.AddRange(new[] { AfterField(member), BeforeField(member), HasField(member) });
        }
        code.AppendLineAt(3, "return new ChangeSet(" + string.Join(", ", args) + ");");
        code.AppendLineAt(2, "}");
    }

    private static void AppendCompose(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment
    )
    {
        _ = runtime;
        _ = optionalFragment;
        code.AppendLineAt(
            2,
            "/// <summary>Composes sequential transitions; overlapping paths must be semantically contiguous.</summary>"
        );
        code.AppendLineAt(2, "public ChangeSet Compose(ChangeSet next)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (next is null) throw new global::System.ArgumentNullException(nameof(next));"
        );
        code.AppendLineAt(3, "if (IsEmpty) return next;");
        code.AppendLineAt(3, "if (next.IsEmpty) return this;");
        code.AppendLineAt(3, "if (__sparse_hasWhole || next.__sparse_hasWhole)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (__sparse_hasWhole && next.__sparse_hasWhole)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (!Fragment.__SparseAreEqual(__sparse_wholeAfter, next.__sparse_wholeBefore)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(5, "return Between(__sparse_wholeBefore, next.__sparse_wholeAfter);");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "throw new global::System.InvalidOperationException(\"ChangeSet composition of whole-root and memberwise transitions requires a shared baseline; this sparse algebra is completed in #97.\");"
        );
        code.AppendLineAt(3, "}");
        foreach (var member in members)
        {
            if (!IsNested(member))
                continue;
            var child = ChildChangeSet(member);
            code.AppendLineAt(3, child + "? __c" + member.Id + ";");
            code.AppendLineAt(
                3,
                "if ("
                    + NestedField(member)
                    + " is null) __c"
                    + member.Id
                    + " = next."
                    + NestedField(member)
                    + ";"
            );
            code.AppendLineAt(
                3,
                "else if (next."
                    + NestedField(member)
                    + " is null) __c"
                    + member.Id
                    + " = "
                    + NestedField(member)
                    + ";"
            );
            code.AppendLineAt(
                3,
                "else { var __cc"
                    + member.Id
                    + " = "
                    + NestedField(member)
                    + ".Compose(next."
                    + NestedField(member)
                    + "); __c"
                    + member.Id
                    + " = __cc"
                    + member.Id
                    + ".IsEmpty ? null : __cc"
                    + member.Id
                    + "; }"
            );
        }
        // Emit per-member merge with explicit locals.
        foreach (var member in members)
        {
            if (IsNested(member))
                continue;
            var opt =
                SparseFragmentPatchEmitter.Runtime + "Optional<" + FragmentValueType(member) + ">";
            code.AppendLineAt(3, opt + " __cb" + member.Id + "_b = default;");
            code.AppendLineAt(3, opt + " __cb" + member.Id + "_a = default;");
            code.AppendLineAt(3, "bool __cb" + member.Id + "_has;");
            code.AppendLineAt(3, "if (!" + HasField(member) + ")");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "__cb" + member.Id + "_has = next." + HasField(member) + ";");
            code.AppendLineAt(4, "__cb" + member.Id + "_b = next." + BeforeField(member) + ";");
            code.AppendLineAt(4, "__cb" + member.Id + "_a = next." + AfterField(member) + ";");
            code.AppendLineAt(3, "}");
            code.AppendLineAt(3, "else if (!next." + HasField(member) + ")");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "__cb" + member.Id + "_has = true;");
            code.AppendLineAt(4, "__cb" + member.Id + "_b = " + BeforeField(member) + ";");
            code.AppendLineAt(4, "__cb" + member.Id + "_a = " + AfterField(member) + ";");
            code.AppendLineAt(3, "}");
            code.AppendLineAt(3, "else");
            code.AppendLineAt(3, "{");
            if (IsKeyed(member) || IsDict(member))
            {
                var coll = "Patch." + SparseFragmentPatchEmitter.CollectionPatch(member);
                code.AppendLineAt(
                    4,
                    "if (!"
                        + coll
                        + ".Between("
                        + AfterField(member)
                        + ", next."
                        + BeforeField(member)
                        + ").__SparseIsEmpty()) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
                );
                code.AppendLineAt(
                    4,
                    "if ("
                        + coll
                        + ".Between("
                        + BeforeField(member)
                        + ", next."
                        + AfterField(member)
                        + ").__SparseIsEmpty()) { __cb"
                        + member.Id
                        + "_has = false; }"
                );
            }
            else
            {
                code.AppendLineAt(
                    4,
                    "if (!Fragment.__SparseEqual_"
                        + member.Id
                        + "("
                        + AfterField(member)
                        + ", next."
                        + BeforeField(member)
                        + ")) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
                );
                code.AppendLineAt(
                    4,
                    "if (Fragment.__SparseEqual_"
                        + member.Id
                        + "("
                        + BeforeField(member)
                        + ", next."
                        + AfterField(member)
                        + ")) { __cb"
                        + member.Id
                        + "_has = false; }"
                );
            }
            code.AppendLineAt(
                4,
                "else { __cb"
                    + member.Id
                    + "_has = true; __cb"
                    + member.Id
                    + "_b = "
                    + BeforeField(member)
                    + "; __cb"
                    + member.Id
                    + "_a = next."
                    + AfterField(member)
                    + "; }"
            );
            code.AppendLineAt(3, "}");
        }
        var args = new List<string> { "false", "default", "default" };
        foreach (var member in members)
        {
            if (IsNested(member))
                args.Add("__c" + member.Id);
            else
                args.AddRange(
                    new[]
                    {
                        "__cb" + member.Id + "_b",
                        "__cb" + member.Id + "_a",
                        "__cb" + member.Id + "_has",
                    }
                );
        }
        code.AppendLineAt(3, "return new ChangeSet(" + string.Join(", ", args) + ");");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(2, "/// <summary>Composes two sequential change sets.</summary>");
        code.AppendLineAt(2, "public static ChangeSet Compose(ChangeSet first, ChangeSet second)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (first is null) throw new global::System.ArgumentNullException(nameof(first));"
        );
        code.AppendLineAt(3, "return first.Compose(second);");
        code.AppendLineAt(2, "}");
    }

    private static void AppendRebase(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment,
        string rebaseResult,
        string prefix,
        string rebase,
        string between
    )
    {
        _ = between;
        _ = prefix;
        code.AppendLineAt(
            2,
            "private static "
                + runtime
                + "Optional<object?> __SparseState("
                + optionalFragment
                + " state) => state.IsPresent ? "
                + runtime
                + "Optional<object?>.Present((object?)state.Value) : "
                + runtime
                + "Optional<object?>.Missing;"
        );
        code.AppendLineAt(
            2,
            "private static "
                + runtime
                + "Optional<object?> __SparseMember<T>("
                + runtime
                + "Optional<T> value) => value.IsPresent ? "
                + runtime
                + "Optional<object?>.Present((object?)value.Value) : "
                + runtime
                + "Optional<object?>.Missing;"
        );
        code.AppendLineAt(
            2,
            "/// <summary>Rebases this change onto a newer state without requiring the original baseline.</summary>"
        );
        code.AppendLineAt(
            2,
            "public " + rebaseResult + " RebaseOnto(" + optionalFragment + " current)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (__sparse_hasWhole)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var __local = ToPatch();");
        code.AppendLineAt(4, "var __rb = " + rebase + "(__sparse_wholeBefore, __local, current);");
        code.AppendLineAt(4, "if (__rb.Conflicts.Count == 0 && __rb.Patch.__SparseIsEmpty())");
        code.AppendLineAt(5, "return " + rebaseResult + ".Success(Between(current, current));");
        code.AppendLineAt(4, "var __ra = __rb.Patch.Apply(current);");
        code.AppendLineAt(
            4,
            "return new " + rebaseResult + "(Between(current, __ra), __rb.Conflicts);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "if (IsEmpty) return " + rebaseResult + ".Success(Between(current, current));"
        );
        code.AppendLineAt(3, "if (!current.IsPresent || current.Value is null)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "var __conf = new global::System.Collections.Generic.List<global::SparseFragments.SparsePatchConflict>();"
        );
        code.AppendLineAt(
            4,
            "__conf.Add(new global::SparseFragments.SparsePatchConflict(new string[0], global::SparseFragments.SparsePatchConflictKind.WholeContribution, __SparseState(__sparse_hasWhole ? __sparse_wholeBefore : default), __SparseState(__sparse_hasWhole ? __sparse_wholeAfter : default), __SparseState(current), \"The contribution conflicts with a concurrent change.\"));"
        );
        code.AppendLineAt(4, "return new " + rebaseResult + "(Between(current, current), __conf);");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "var __cur = current.Value!;");
        code.AppendLineAt(
            3,
            "var __conflicts = new global::System.Collections.Generic.List<global::SparseFragments.SparsePatchConflict>();"
        );
        // Declare rebased locals.
        foreach (var member in members)
        {
            if (IsNested(member))
            {
                code.AppendLineAt(3, ChildChangeSet(member) + "? __r" + member.Id + " = null;");
            }
            else
            {
                var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
                code.AppendLineAt(3, opt + " __rb" + member.Id + " = default;");
                code.AppendLineAt(3, opt + " __ra" + member.Id + " = default;");
                code.AppendLineAt(3, "bool __rh" + member.Id + " = false;");
            }
        }
        foreach (var member in members)
        {
            var prop = member.Property.Name;
            var lit = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(prop, true);
            var esc = SparseNaming.EscapeIdentifier(prop);
            code.AppendLineAt(3, "{");
            if (IsNested(member))
            {
                code.AppendLineAt(4, "if (" + NestedField(member) + " is not null)");
                code.AppendLineAt(4, "{");
                code.AppendLineAt(
                    5,
                    "var __nr"
                        + member.Id
                        + " = "
                        + NestedField(member)
                        + ".RebaseOnto(__cur."
                        + esc
                        + ");"
                );
                code.AppendLineAt(5, "foreach (var __c in __nr" + member.Id + ".Conflicts)");
                code.AppendLineAt(5, "{");
                code.AppendLineAt(6, "__conflicts.Add(__c.WithPathPrefix(" + lit + "));");
                code.AppendLineAt(5, "}");
                code.AppendLineAt(
                    5,
                    "__r"
                        + member.Id
                        + " = __nr"
                        + member.Id
                        + ".Patch.IsEmpty ? null : __nr"
                        + member.Id
                        + ".Patch;"
                );
                code.AppendLineAt(4, "}");
            }
            else if (IsKeyed(member) || IsDict(member))
            {
                var coll = "Patch." + SparseFragmentPatchEmitter.CollectionPatch(member);
                code.AppendLineAt(4, "if (" + HasField(member) + ")");
                code.AppendLineAt(4, "{");
                code.AppendLineAt(
                    4,
                    "    var __base" + member.Id + " = " + BeforeField(member) + ";"
                );
                code.AppendLineAt(4, "    var __curM" + member.Id + " = __cur." + esc + ";");
                code.AppendLineAt(
                    4,
                    "    var __local"
                        + member.Id
                        + " = "
                        + coll
                        + ".Between(__base"
                        + member.Id
                        + ", "
                        + AfterField(member)
                        + ");"
                );
                code.AppendLineAt(
                    4,
                    "    var __cr"
                        + member.Id
                        + " = "
                        + coll
                        + ".Rebase(__base"
                        + member.Id
                        + ", __local"
                        + member.Id
                        + ", __curM"
                        + member.Id
                        + ");"
                );
                code.AppendLineAt(4, "    foreach (var __c in __cr" + member.Id + ".Conflicts)");
                code.AppendLineAt(4, "    {");
                code.AppendLineAt(5, "        __conflicts.Add(__c.WithPathPrefix(" + lit + "));");
                code.AppendLineAt(4, "    }");
                code.AppendLineAt(4, "    if (!__cr" + member.Id + ".Patch.__SparseIsEmpty())");
                code.AppendLineAt(4, "    {");
                code.AppendLineAt(
                    5,
                    "        var __applied"
                        + member.Id
                        + " = __cr"
                        + member.Id
                        + ".Patch.Apply(__curM"
                        + member.Id
                        + ");"
                );
                code.AppendLineAt(
                    5,
                    "        if (!Fragment.__SparseEqual_"
                        + member.Id
                        + "(__curM"
                        + member.Id
                        + ", __applied"
                        + member.Id
                        + "))"
                );
                code.AppendLineAt(5, "        {");
                code.AppendLineAt(6, "            __rh" + member.Id + " = true;");
                code.AppendLineAt(
                    6,
                    "            __rb" + member.Id + " = __curM" + member.Id + ";"
                );
                code.AppendLineAt(
                    6,
                    "            __ra" + member.Id + " = __applied" + member.Id + ";"
                );
                code.AppendLineAt(5, "        }");
                code.AppendLineAt(4, "    }");
                code.AppendLineAt(4, "}");
            }
            else if (member.MergeStrategyType is not null)
            {
                var strat = "Fragment." + SparseWellKnownNames.MergeStrategyFieldPrefix + member.Id;
                code.AppendLineAt(4, "if (" + HasField(member) + ")");
                code.AppendLineAt(4, "{");
                code.AppendLineAt(
                    4,
                    "    var __base" + member.Id + " = " + BeforeField(member) + ";"
                );
                code.AppendLineAt(
                    4,
                    "    var __des" + member.Id + " = " + AfterField(member) + ";"
                );
                code.AppendLineAt(4, "    var __curM" + member.Id + " = __cur." + esc + ";");
                code.AppendLineAt(
                    4,
                    "    if ("
                        + strat
                        + ".TryRebase(__base"
                        + member.Id
                        + ", __des"
                        + member.Id
                        + ", __curM"
                        + member.Id
                        + ", out var __reb"
                        + member.Id
                        + ", out var __reason"
                        + member.Id
                        + "))"
                );
                code.AppendLineAt(4, "    {");
                code.AppendLineAt(
                    5,
                    "        if (!((!__reb"
                        + member.Id
                        + ".IsPresent && !__curM"
                        + member.Id
                        + ".IsPresent) || (__reb"
                        + member.Id
                        + ".IsPresent && __curM"
                        + member.Id
                        + ".IsPresent && "
                        + strat
                        + ".AreEqual(__curM"
                        + member.Id
                        + ".Value, __reb"
                        + member.Id
                        + ".Value))))"
                );
                code.AppendLineAt(5, "        {");
                code.AppendLineAt(6, "            __rh" + member.Id + " = true;");
                code.AppendLineAt(
                    6,
                    "            __rb" + member.Id + " = __curM" + member.Id + ";"
                );
                code.AppendLineAt(6, "            __ra" + member.Id + " = __reb" + member.Id + ";");
                code.AppendLineAt(5, "        }");
                code.AppendLineAt(4, "    }");
                code.AppendLineAt(4, "    else");
                code.AppendLineAt(4, "    {");
                code.AppendLineAt(
                    5,
                    "        __conflicts.Add(new global::SparseFragments.SparsePatchConflict(new string[] { "
                        + lit
                        + " }, global::SparseFragments.SparsePatchConflictKind.CustomStrategy, __SparseMember(__base"
                        + member.Id
                        + "), __SparseMember(__des"
                        + member.Id
                        + "), __SparseMember(__curM"
                        + member.Id
                        + "), __reason"
                        + member.Id
                        + " ?? \"The custom merge strategy could not rebase the member.\"));"
                );
                code.AppendLineAt(4, "    }");
                code.AppendLineAt(4, "}");
            }
            else
            {
                string kind;
                if (member.MergeMode == 2)
                    kind = "global::SparseFragments.SparsePatchConflictKind.CollectionAppend";
                else if (member.MergeMode == 3)
                    kind = "global::SparseFragments.SparsePatchConflictKind.CollectionSetUnion";
                else
                    kind = "global::SparseFragments.SparsePatchConflictKind.Scalar";
                code.AppendLineAt(4, "if (" + HasField(member) + ")");
                code.AppendLineAt(4, "{");
                code.AppendLineAt(
                    4,
                    "    var __base" + member.Id + " = " + BeforeField(member) + ";"
                );
                code.AppendLineAt(
                    4,
                    "    var __des" + member.Id + " = " + AfterField(member) + ";"
                );
                code.AppendLineAt(4, "    var __curM" + member.Id + " = __cur." + esc + ";");
                code.AppendLineAt(
                    4,
                    "    if (Fragment.__SparseEqual_"
                        + member.Id
                        + "(__base"
                        + member.Id
                        + ", __curM"
                        + member.Id
                        + "))"
                );
                code.AppendLineAt(4, "    {");
                code.AppendLineAt(5, "        __rh" + member.Id + " = true;");
                code.AppendLineAt(5, "        __rb" + member.Id + " = __curM" + member.Id + ";");
                code.AppendLineAt(5, "        __ra" + member.Id + " = __des" + member.Id + ";");
                code.AppendLineAt(4, "    }");
                code.AppendLineAt(
                    4,
                    "    else if (!Fragment.__SparseEqual_"
                        + member.Id
                        + "(__des"
                        + member.Id
                        + ", __curM"
                        + member.Id
                        + "))"
                );
                code.AppendLineAt(4, "    {");
                code.AppendLineAt(
                    5,
                    "        __conflicts.Add(new global::SparseFragments.SparsePatchConflict(new string[] { "
                        + lit
                        + " }, "
                        + kind
                        + ", __SparseMember(__base"
                        + member.Id
                        + "), __SparseMember(__des"
                        + member.Id
                        + "), __SparseMember(__curM"
                        + member.Id
                        + "), \"The member conflicts with a concurrent change.\"));"
                );
                code.AppendLineAt(4, "    }");
                code.AppendLineAt(4, "}");
            }
            code.AppendLineAt(3, "}");
        }
        var rargs = new List<string> { "false", "default", "default" };
        foreach (var member in members)
        {
            if (IsNested(member))
                rargs.Add("__r" + member.Id);
            else
                rargs.AddRange(
                    new[] { "__rb" + member.Id, "__ra" + member.Id, "__rh" + member.Id }
                );
        }
        code.AppendLineAt(3, "var __rebased = new ChangeSet(" + string.Join(", ", rargs) + ");");
        code.AppendLineAt(3, "return new " + rebaseResult + "(__rebased, __conflicts);");
        code.AppendLineAt(2, "}");
    }

    private static void AppendTypedSurface(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members
    )
    {
        if (members.IsDefaultOrEmpty)
            return;
        var runtime = SparseFragmentPatchEmitter.Runtime;
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
        var propNames = new Dictionary<int, string>();
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
        var transNames = new Dictionary<int, string>();
        foreach (var member in members)
        {
            if (IsNested(member))
                continue;
            var prefix = new System.Text.StringBuilder();
            while (usedTypes.Contains(prefix.ToString() + propNames[member.Id] + "Transition"))
                prefix.Append("Sparse");
            var t = prefix.ToString() + propNames[member.Id] + "Transition";
            usedTypes.Add(t);
            transNames[member.Id] = t;
        }
        // Sparse before/after helpers read canonical sparse storage (issue #96).
        // Whole-root transitions project member states from the retained root
        // fragments; memberwise transitions expose only retained changed paths.
        foreach (var member in members)
        {
            if (IsNested(member))
                continue;
            var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
            var esc = SparseNaming.EscapeIdentifier(member.Property.Name);
            code.AppendLineAt(
                2,
                "private "
                    + opt
                    + " __SparseBefore_"
                    + member.Id
                    + "() => __sparse_hasWhole ? (__sparse_wholeBefore.IsPresent && __sparse_wholeBefore.Value is not null ? __sparse_wholeBefore.Value."
                    + esc
                    + " : default) : ("
                    + HasField(member)
                    + " ? "
                    + BeforeField(member)
                    + " : default);"
            );
            code.AppendLineAt(
                2,
                "private "
                    + opt
                    + " __SparseAfter_"
                    + member.Id
                    + "() => __sparse_hasWhole ? (__sparse_wholeAfter.IsPresent && __sparse_wholeAfter.Value is not null ? __sparse_wholeAfter.Value."
                    + esc
                    + " : default) : ("
                    + HasField(member)
                    + " ? "
                    + AfterField(member)
                    + " : default);"
            );
        }
        foreach (var member in members)
        {
            var prop = SparseNaming.EscapeIdentifier(propNames[member.Id]);
            if (IsScalar(member))
                AppendScalarTransition(code, member, prop, transNames[member.Id], runtime);
            else if (IsNested(member))
                AppendNestedProperty(code, member, prop);
            else if (IsKeyed(member))
                AppendKeyedTransition(code, member, prop, transNames[member.Id], runtime);
            else if (IsDict(member))
                AppendDictTransition(code, member, prop, transNames[member.Id], runtime);
        }
    }

    private static void AppendScalarTransition(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string prop,
        string trans,
        string runtime
    )
    {
        var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
        code.AppendLineAt(
            2,
            "/// <summary>Typed transition for member '" + member.Property.Name + "'.</summary>"
        );
        code.AppendLineAt(2, "public readonly struct " + trans);
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "private readonly " + opt + " _before;");
        code.AppendLineAt(3, "private readonly " + opt + " _after;");
        code.AppendLineAt(3, "private readonly bool _isChanged;");
        code.AppendLineAt(
            3,
            "internal " + trans + "(" + opt + " before, " + opt + " after, bool isChanged)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "_before = before;");
        code.AppendLineAt(4, "_after = after;");
        code.AppendLineAt(4, "_isChanged = isChanged;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "public bool IsChanged => _isChanged;");
        code.AppendLineAt(3, "public " + opt + " Before => _before;");
        code.AppendLineAt(3, "public " + opt + " After => _after;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Gets the typed transition for member '"
                + member.Property.Name
                + "'.</summary>"
        );
        code.AppendLineAt(2, "[global::System.Text.Json.Serialization.JsonIgnore]");
        code.AppendLineAt(2, "public " + trans + " " + prop);
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "get");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var __b = __SparseBefore_" + member.Id + "();");
        code.AppendLineAt(4, "var __a = __SparseAfter_" + member.Id + "();");
        code.AppendLineAt(
            4,
            "return new "
                + trans
                + "(__b, __a, !Fragment.__SparseEqual_"
                + member.Id
                + "(__b, __a));"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
    }

    private static void AppendNestedProperty(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string prop
    )
    {
        var childCs = ChildChangeSet(member);
        var esc = SparseNaming.EscapeIdentifier(member.Property.Name);
        code.AppendLineAt(
            2,
            "/// <summary>Gets the nested typed change set for member '"
                + member.Property.Name
                + "'.</summary>"
        );
        code.AppendLineAt(2, "[global::System.Text.Json.Serialization.JsonIgnore]");
        code.AppendLineAt(2, "public " + childCs + " " + prop);
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "get");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (__sparse_hasWhole)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "var __wb = __sparse_wholeBefore.IsPresent && __sparse_wholeBefore.Value is not null ? __sparse_wholeBefore.Value."
                + esc
                + " : default;"
        );
        code.AppendLineAt(
            5,
            "var __wa = __sparse_wholeAfter.IsPresent && __sparse_wholeAfter.Value is not null ? __sparse_wholeAfter.Value."
                + esc
                + " : default;"
        );
        code.AppendLineAt(5, "return " + childCs + ".Between(__wb, __wa);");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "return " + NestedField(member) + " ?? " + childCs + ".Between(default, default);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
    }

    private static string KeyOfBody(SparseMemberModel member)
    {
        if (member.Collection.KeyKind == SparseKeyKind.Interface)
            return "return element.SparseKey;";
        var keys = member.Collection.KeyPropertyNames;
        if (keys.Length == 1)
            return "return element." + SparseNaming.EscapeIdentifier(keys[0]) + ";";
        var builder = new System.Text.StringBuilder("(");
        for (var i = 0; i < keys.Length; i++)
        {
            if (i > 0)
                builder.Append(", ");
            builder.Append("element.").Append(SparseNaming.EscapeIdentifier(keys[i]));
        }
        builder.Append(")");
        return "return " + builder.ToString() + ";";
    }

    private static void AppendKeyedTransition(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string prop,
        string trans,
        string runtime
    )
    {
        var keyType = KeyTypeOf(member);
        var elementType = ElementTypeOf(member);
        var listType = member.Property.Type.Name;
        var optList = runtime + "Optional<" + listType + ">";
        var optElement = runtime + "Optional<" + elementType + ">";
        var elementCs = ElementChangeSetOf(member);
        var elementFrag = ElementFragmentOf(member);
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var facade = "global::SparseFragments.CompilerServices.SparseFragmentRuntime";
        var readOnlyList = "global::System.Collections.Generic.IReadOnlyList<";
        var readOnlyDict = "global::System.Collections.Generic.IReadOnlyDictionary<";
        // Transition type.
        code.AppendLineAt(
            2,
            "/// <summary>Typed keyed transition for member '"
                + member.Property.Name
                + "'.</summary>"
        );
        code.AppendLineAt(
            2,
            "public sealed class "
                + trans
                + " : global::System.Collections.Generic.IEnumerable<"
                + trans
                + ".Item>"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "private readonly global::System.Collections.Generic.IReadOnlyList<Item> _items;"
        );
        code.AppendLineAt(
            3,
            "private global::System.Collections.Generic.Dictionary<" + keyType + ", Item>? _lookup;"
        );
        code.AppendLineAt(
            3,
            "internal "
                + trans
                + "("
                + optList
                + " before, "
                + optList
                + " after, global::System.Collections.Generic.List<"
                + elementType
                + "> added, global::System.Collections.Generic.List<"
                + elementType
                + "> removed, global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementCs
                + "> edited, global::System.Collections.Generic.List<"
                + keyType
                + "> beforeOrder, global::System.Collections.Generic.List<"
                + keyType
                + "> afterOrder, bool orderChanged, global::System.Collections.Generic.List<Item> items, bool isEmpty)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "Before = before;");
        code.AppendLineAt(4, "After = after;");
        code.AppendLineAt(4, "Added = added.AsReadOnly();");
        code.AppendLineAt(4, "Removed = removed.AsReadOnly();");
        code.AppendLineAt(
            4,
            "Edited = new global::System.Collections.ObjectModel.ReadOnlyDictionary<"
                + keyType
                + ", "
                + elementCs
                + ">(edited);"
        );
        code.AppendLineAt(4, "BeforeOrder = beforeOrder.AsReadOnly();");
        code.AppendLineAt(4, "AfterOrder = afterOrder.AsReadOnly();");
        code.AppendLineAt(4, "OrderChanged = orderChanged;");
        code.AppendLineAt(4, "_items = items.AsReadOnly();");
        code.AppendLineAt(4, "IsEmpty = isEmpty;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "public bool IsEmpty { get; }");
        code.AppendLineAt(3, "public bool IsChanged => !IsEmpty;");
        code.AppendLineAt(3, "public " + optList + " Before { get; }");
        code.AppendLineAt(3, "public " + optList + " After { get; }");
        code.AppendLineAt(3, "public " + readOnlyList + elementType + "> Added { get; }");
        code.AppendLineAt(3, "public " + readOnlyList + elementType + "> Removed { get; }");
        code.AppendLineAt(
            3,
            "public " + readOnlyDict + keyType + ", " + elementCs + "> Edited { get; }"
        );
        code.AppendLineAt(3, "public " + readOnlyList + keyType + "> BeforeOrder { get; }");
        code.AppendLineAt(3, "public " + readOnlyList + keyType + "> AfterOrder { get; }");
        code.AppendLineAt(3, "public bool OrderChanged { get; }");
        code.AppendLineAt(
            3,
            "public global::System.Collections.Generic.IEnumerator<Item> GetEnumerator() => _items.GetEnumerator();"
        );
        code.AppendLineAt(
            3,
            "global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();"
        );
        code.AppendLineAt(3, "/// <summary>A single typed keyed item change.</summary>");
        code.AppendLineAt(
            3,
            "/// <remarks>Empty lookup results expose <see cref=\"IsEmpty\"/> and are never enumerated.</remarks>"
        );
        code.AppendLineAt(3, "public sealed class Item");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "internal Item("
                + keyType
                + " key, "
                + optElement
                + " before, "
                + optElement
                + " after, int beforeIndex, int afterIndex, bool isAdded, bool isRemoved, bool isEdited, bool isReordered, "
                + elementCs
                + " edit, bool isEmpty)"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "Key = key;");
        code.AppendLineAt(5, "Before = before;");
        code.AppendLineAt(5, "After = after;");
        code.AppendLineAt(5, "BeforeIndex = beforeIndex;");
        code.AppendLineAt(5, "AfterIndex = afterIndex;");
        code.AppendLineAt(5, "IsAdded = isAdded;");
        code.AppendLineAt(5, "IsRemoved = isRemoved;");
        code.AppendLineAt(5, "IsEdited = isEdited;");
        code.AppendLineAt(5, "IsReordered = isReordered;");
        code.AppendLineAt(5, "Edit = edit;");
        code.AppendLineAt(5, "IsEmpty = isEmpty;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "public " + keyType + " Key { get; }");
        code.AppendLineAt(4, "public " + optElement + " Before { get; }");
        code.AppendLineAt(4, "public " + optElement + " After { get; }");
        code.AppendLineAt(4, "public int BeforeIndex { get; }");
        code.AppendLineAt(4, "public int AfterIndex { get; }");
        code.AppendLineAt(4, "public bool IsAdded { get; }");
        code.AppendLineAt(4, "public bool IsRemoved { get; }");
        code.AppendLineAt(4, "public bool IsEdited { get; }");
        code.AppendLineAt(4, "public bool IsReordered { get; }");
        code.AppendLineAt(4, "public " + elementCs + " Edit { get; }");
        code.AppendLineAt(
            4,
            "/// <summary>Whether this item carries no semantic change for the requested key.</summary>"
        );
        code.AppendLineAt(4, "public bool IsEmpty { get; }");
        code.AppendLineAt(4, "public bool IsChanged => !IsEmpty;");
        code.AppendLineAt(
            4,
            "/// <summary>Shared allocation-light empty item; retains no element snapshots.</summary>"
        );
        code.AppendLineAt(
            4,
            "public static Item Empty { get; } = new Item(default!, default, default, -1, -1, false, false, false, false, "
                + elementCs
                + ".Between(default, default), true);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "/// <summary>Looks up the typed change for a stable key; never returns null.</summary>"
        );
        code.AppendLineAt(
            3,
            "/// <remarks>Unchanged or unknown keys return <see cref=\"Item.Empty\"/> (allocation-light singleton shared across lookups). "
                + "Non-empty results are the same instances produced by enumeration. "
                + "BeforeIndex/AfterIndex are absolute collection indexes; IsReordered observes surviving-key relative rank.</remarks>"
        );
        code.AppendLineAt(3, "public Item GetChange(" + keyType + " key)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var __lookup = _lookup;");
        code.AppendLineAt(4, "if (__lookup is null)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "__lookup = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", Item>("
                + comparer
                + ");"
        );
        code.AppendLineAt(5, "foreach (var __item in _items) __lookup[__item.Key] = __item;");
        code.AppendLineAt(5, "_lookup = __lookup;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "return __lookup.TryGetValue(key, out var __found) ? __found : Item.Empty;"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
        // Key helper.
        code.AppendLineAt(
            2,
            "private static "
                + keyType
                + " __SparseKeyOf_ChangeSet_"
                + member.Id
                + "("
                + elementType
                + " element)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if ((object?)element is null) throw new global::System.InvalidOperationException(\"Null elements have no stable key.\");"
        );
        code.AppendLineAt(3, KeyOfBody(member));
        code.AppendLineAt(2, "}");
        // Build helper (semantic before/after projection over sparse storage; never reads patch ops).
        code.AppendLineAt(
            2,
            "private "
                + trans
                + " __SparseBuild_"
                + member.Id
                + "("
                + optList
                + " before, "
                + optList
                + " after)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var __comparer = " + comparer + ";");
        code.AppendLineAt(
            3,
            "bool __beforeHas = before.IsPresent && (object?)before.Value is not null;"
        );
        code.AppendLineAt(
            3,
            "bool __afterHas = after.IsPresent && (object?)after.Value is not null;"
        );
        code.AppendLineAt(3, "if (!__beforeHas && !__afterHas)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "bool __eq = Fragment.__SparseEqual_" + member.Id + "(before, after);"
        );
        code.AppendLineAt(
            4,
            "return new "
                + trans
                + "(before, after, new global::System.Collections.Generic.List<"
                + elementType
                + ">(), new global::System.Collections.Generic.List<"
                + elementType
                + ">(), new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementCs
                + ">(__comparer), new global::System.Collections.Generic.List<"
                + keyType
                + ">(), new global::System.Collections.Generic.List<"
                + keyType
                + ">(), false, new global::System.Collections.Generic.List<"
                + trans
                + ".Item>(), __eq);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "var __beforeOrder = new global::System.Collections.Generic.List<" + keyType + ">();"
        );
        code.AppendLineAt(
            3,
            "var __beforeMap = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">(__comparer);"
        );
        code.AppendLineAt(3, "if (__beforeHas) foreach (var __item in before.Value!)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var __k = __SparseKeyOf_ChangeSet_" + member.Id + "(__item);");
        code.AppendLineAt(
            4,
            "if (!__beforeMap.TryAdd(__k, __item)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection.\");"
        );
        code.AppendLineAt(4, "__beforeOrder.Add(__k);");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "var __afterOrder = new global::System.Collections.Generic.List<" + keyType + ">();"
        );
        code.AppendLineAt(
            3,
            "var __afterMap = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">(__comparer);"
        );
        code.AppendLineAt(3, "if (__afterHas) foreach (var __item in after.Value!)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var __k = __SparseKeyOf_ChangeSet_" + member.Id + "(__item);");
        code.AppendLineAt(
            4,
            "if (!__afterMap.TryAdd(__k, __item)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection.\");"
        );
        code.AppendLineAt(4, "__afterOrder.Add(__k);");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "var __added = new global::System.Collections.Generic.List<" + elementType + ">();"
        );
        code.AppendLineAt(
            3,
            "foreach (var __k in __afterOrder) if (!__beforeMap.ContainsKey(__k)) __added.Add(__afterMap[__k]);"
        );
        code.AppendLineAt(
            3,
            "var __removed = new global::System.Collections.Generic.List<" + elementType + ">();"
        );
        code.AppendLineAt(
            3,
            "foreach (var __k in __beforeOrder) if (!__afterMap.ContainsKey(__k)) __removed.Add(__beforeMap[__k]);"
        );
        code.AppendLineAt(
            3,
            "var __edited = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementCs
                + ">(__comparer);"
        );
        code.AppendLineAt(3, "foreach (var __k in __beforeMap.Keys)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (!__afterMap.TryGetValue(__k, out var __afterItem)) continue;");
        code.AppendLineAt(4, "var __beforeItem = __beforeMap[__k];");
        code.AppendLineAt(
            4,
            "var __nested = "
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__beforeItem)), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__afterItem)));"
        );
        code.AppendLineAt(4, "if (!__nested.IsEmpty) __edited[__k] = __nested;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "bool __orderChanged = !"
                + facade
                + ".KeyOrderEquals<"
                + keyType
                + ">(__beforeOrder, __afterOrder);"
        );
        code.AppendLineAt(
            3,
            "bool __empty = __added.Count == 0 && __removed.Count == 0 && __edited.Count == 0 && !__orderChanged;"
        );
        // Surviving-rank reorder set (relative order, not absolute index shifts).
        code.AppendLineAt(
            3,
            "var __beforeRank = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", int>(__comparer);"
        );
        code.AppendLineAt(
            3,
            "{ var __r = 0; foreach (var __k in __beforeOrder) if (__afterMap.ContainsKey(__k)) __beforeRank[__k] = __r++; }"
        );
        code.AppendLineAt(
            3,
            "var __afterRank = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", int>(__comparer);"
        );
        code.AppendLineAt(
            3,
            "{ var __r = 0; foreach (var __k in __afterOrder) if (__beforeMap.ContainsKey(__k)) __afterRank[__k] = __r++; }"
        );
        code.AppendLineAt(
            3,
            "var __reordered = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(__comparer);"
        );
        code.AppendLineAt(
            3,
            "foreach (var __kv in __beforeRank) if (__afterRank.TryGetValue(__kv.Key, out var __ar) && __ar != __kv.Value) __reordered.Add(__kv.Key);"
        );
        code.AppendLineAt(
            3,
            "var __beforeIndex = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", int>(__comparer);"
        );
        code.AppendLineAt(
            3,
            "for (var __i = 0; __i < __beforeOrder.Count; __i++) __beforeIndex[__beforeOrder[__i]] = __i;"
        );
        code.AppendLineAt(
            3,
            "var __afterIndex = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", int>(__comparer);"
        );
        code.AppendLineAt(
            3,
            "for (var __i = 0; __i < __afterOrder.Count; __i++) __afterIndex[__afterOrder[__i]] = __i;"
        );
        code.AppendLineAt(
            3,
            "var __items = new global::System.Collections.Generic.List<" + trans + ".Item>();"
        );
        code.AppendLineAt(3, "foreach (var __k in __afterOrder)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "bool __inBefore = __beforeMap.TryGetValue(__k, out var __b);");
        code.AppendLineAt(4, "var __a = __afterMap[__k];");
        code.AppendLineAt(4, "bool __isEdited = __edited.TryGetValue(__k, out var __edit);");
        code.AppendLineAt(4, "bool __isReordered = __reordered.Contains(__k);");
        code.AppendLineAt(4, "if (!__inBefore || __isEdited || __isReordered)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            runtime
                + "Optional<"
                + elementFrag
                + "?> __eb = __inBefore ? "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__b!)) : default;"
        );
        code.AppendLineAt(
            5,
            runtime
                + "Optional<"
                + elementFrag
                + "?> __ea = "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a));"
        );
        code.AppendLineAt(
            5,
            "var __fullEdit = __isEdited ? __edit! : " + elementCs + ".Between(__eb, __ea);"
        );
        code.AppendLineAt(
            5,
            optElement
                + " __ib = __inBefore ? "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__b!) : default;"
        );
        code.AppendLineAt(
            5,
            "int __bi = __beforeIndex.TryGetValue(__k, out var __bv) ? __bv : -1;"
        );
        code.AppendLineAt(5, "int __ai = __afterIndex.TryGetValue(__k, out var __av) ? __av : -1;");
        code.AppendLineAt(
            5,
            "__items.Add(new "
                + trans
                + ".Item(__k, __ib, "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__a), __bi, __ai, !__inBefore, false, __isEdited, __isReordered, __fullEdit, false));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "foreach (var __k in __beforeOrder)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (__afterMap.ContainsKey(__k)) continue;");
        code.AppendLineAt(4, "var __b = __beforeMap[__k];");
        code.AppendLineAt(
            4,
            runtime
                + "Optional<"
                + elementFrag
                + "?> __eb = "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__b));"
        );
        code.AppendLineAt(4, "var __fullEdit = " + elementCs + ".Between(__eb, default);");
        code.AppendLineAt(
            4,
            "int __bi = __beforeIndex.TryGetValue(__k, out var __bv) ? __bv : -1;"
        );
        code.AppendLineAt(
            4,
            "__items.Add(new "
                + trans
                + ".Item(__k, "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__b), default, __bi, -1, false, true, false, false, __fullEdit, false));"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "return new "
                + trans
                + "(before, after, __added, __removed, __edited, __beforeOrder, __afterOrder, __orderChanged, __items, __empty);"
        );
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Gets the typed keyed transition for member '"
                + member.Property.Name
                + "'.</summary>"
        );
        code.AppendLineAt(2, "[global::System.Text.Json.Serialization.JsonIgnore]");
        code.AppendLineAt(
            2,
            "public "
                + trans
                + " "
                + prop
                + " => __SparseBuild_"
                + member.Id
                + "(__SparseBefore_"
                + member.Id
                + "(), __SparseAfter_"
                + member.Id
                + "());"
        );
    }

    private static void AppendDictTransition(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string prop,
        string trans,
        string runtime
    )
    {
        var keyType = KeyTypeOf(member);
        var valueType = ValueTypeOf(member);
        var dictType = member.Property.Type.Name;
        var optDict = runtime + "Optional<" + dictType + ">";
        var optValue = runtime + "Optional<" + valueType + ">";
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var facade = "global::SparseFragments.CompilerServices.SparseFragmentRuntime";
        var readOnlyDict = "global::System.Collections.Generic.IReadOnlyDictionary<";
        var hasPatch = member.Collection.ValueType?.IsFragmentModel == true;
        var valueCs = hasPatch ? ValueChangeSetOf(member) : null;
        var valueFrag = hasPatch ? ValueFragmentOf(member) : null;
        var editedType = hasPatch ? valueCs! : valueType;
        code.AppendLineAt(
            2,
            "/// <summary>Typed dictionary transition for member '"
                + member.Property.Name
                + "'.</summary>"
        );
        code.AppendLineAt(
            2,
            "public sealed class "
                + trans
                + " : global::System.Collections.Generic.IEnumerable<"
                + trans
                + ".Item>"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "private readonly global::System.Collections.Generic.IReadOnlyList<Item> _items;"
        );
        code.AppendLineAt(
            3,
            "private global::System.Collections.Generic.Dictionary<" + keyType + ", Item>? _lookup;"
        );
        code.AppendLineAt(
            3,
            "internal "
                + trans
                + "("
                + optDict
                + " before, "
                + optDict
                + " after, global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + "> added, global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + "> removed, global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + editedType
                + "> edited, global::System.Collections.Generic.List<Item> items, bool isEmpty)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "Before = before;");
        code.AppendLineAt(4, "After = after;");
        code.AppendLineAt(
            4,
            "Added = new global::System.Collections.ObjectModel.ReadOnlyDictionary<"
                + keyType
                + ", "
                + valueType
                + ">(added);"
        );
        code.AppendLineAt(
            4,
            "Removed = new global::System.Collections.ObjectModel.ReadOnlyDictionary<"
                + keyType
                + ", "
                + valueType
                + ">(removed);"
        );
        code.AppendLineAt(
            4,
            "Edited = new global::System.Collections.ObjectModel.ReadOnlyDictionary<"
                + keyType
                + ", "
                + editedType
                + ">(edited);"
        );
        code.AppendLineAt(4, "_items = items.AsReadOnly();");
        code.AppendLineAt(4, "IsEmpty = isEmpty;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "public bool IsEmpty { get; }");
        code.AppendLineAt(3, "public bool IsChanged => !IsEmpty;");
        code.AppendLineAt(3, "public " + optDict + " Before { get; }");
        code.AppendLineAt(3, "public " + optDict + " After { get; }");
        code.AppendLineAt(
            3,
            "public " + readOnlyDict + keyType + ", " + valueType + "> Added { get; }"
        );
        code.AppendLineAt(
            3,
            "public " + readOnlyDict + keyType + ", " + valueType + "> Removed { get; }"
        );
        code.AppendLineAt(
            3,
            "public " + readOnlyDict + keyType + ", " + editedType + "> Edited { get; }"
        );
        code.AppendLineAt(
            3,
            "public global::System.Collections.Generic.IEnumerator<Item> GetEnumerator() => _items.GetEnumerator();"
        );
        code.AppendLineAt(
            3,
            "global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();"
        );
        code.AppendLineAt(3, "/// <summary>A single typed dictionary entry change.</summary>");
        code.AppendLineAt(3, "public sealed class Item");
        code.AppendLineAt(3, "{");
        if (hasPatch)
            code.AppendLineAt(
                4,
                "internal Item("
                    + keyType
                    + " key, "
                    + optValue
                    + " before, "
                    + optValue
                    + " after, bool isAdded, bool isRemoved, bool isEdited, "
                    + valueCs
                    + " edit, bool isEmpty)"
            );
        else
            code.AppendLineAt(
                4,
                "internal Item("
                    + keyType
                    + " key, "
                    + optValue
                    + " before, "
                    + optValue
                    + " after, bool isAdded, bool isRemoved, bool isEdited, bool isEmpty)"
            );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "Key = key;");
        code.AppendLineAt(5, "Before = before;");
        code.AppendLineAt(5, "After = after;");
        code.AppendLineAt(5, "IsAdded = isAdded;");
        code.AppendLineAt(5, "IsRemoved = isRemoved;");
        code.AppendLineAt(5, "IsEdited = isEdited;");
        if (hasPatch)
            code.AppendLineAt(5, "Edit = edit;");
        code.AppendLineAt(5, "IsEmpty = isEmpty;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "public " + keyType + " Key { get; }");
        code.AppendLineAt(4, "public " + optValue + " Before { get; }");
        code.AppendLineAt(4, "public " + optValue + " After { get; }");
        code.AppendLineAt(4, "public bool IsAdded { get; }");
        code.AppendLineAt(4, "public bool IsRemoved { get; }");
        code.AppendLineAt(4, "public bool IsEdited { get; }");
        if (hasPatch)
            code.AppendLineAt(4, "public " + valueCs + " Edit { get; }");
        code.AppendLineAt(
            4,
            "/// <summary>Whether this entry carries no semantic change for the requested key.</summary>"
        );
        code.AppendLineAt(4, "public bool IsEmpty { get; }");
        code.AppendLineAt(4, "public bool IsChanged => !IsEmpty;");
        code.AppendLineAt(
            4,
            "/// <summary>Shared allocation-light empty entry; retains no value snapshots.</summary>"
        );
        if (hasPatch)
            code.AppendLineAt(
                4,
                "public static Item Empty { get; } = new Item(default!, default, default, false, false, false, "
                    + valueCs
                    + ".Between(default, default), true);"
            );
        else
            code.AppendLineAt(
                4,
                "public static Item Empty { get; } = new Item(default!, default, default, false, false, false, true);"
            );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "/// <summary>Looks up the typed change for a dictionary key; never returns null.</summary>"
        );
        code.AppendLineAt(
            3,
            "/// <remarks>Unchanged or unknown keys return <see cref=\"Item.Empty\"/> (allocation-light singleton). "
                + "Non-empty results are the same instances produced by enumeration.</remarks>"
        );
        code.AppendLineAt(3, "public Item GetChange(" + keyType + " key)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var __lookup = _lookup;");
        code.AppendLineAt(4, "if (__lookup is null)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "__lookup = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", Item>("
                + comparer
                + ");"
        );
        code.AppendLineAt(5, "foreach (var __item in _items) __lookup[__item.Key] = __item;");
        code.AppendLineAt(5, "_lookup = __lookup;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "return __lookup.TryGetValue(key, out var __found) ? __found : Item.Empty;"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "private "
                + trans
                + " __SparseBuild_"
                + member.Id
                + "("
                + optDict
                + " before, "
                + optDict
                + " after)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var __comparer = " + comparer + ";");
        code.AppendLineAt(
            3,
            "bool __beforeHas = before.IsPresent && (object?)before.Value is not null;"
        );
        code.AppendLineAt(
            3,
            "bool __afterHas = after.IsPresent && (object?)after.Value is not null;"
        );
        code.AppendLineAt(3, "if (!__beforeHas && !__afterHas)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "bool __eq = Fragment.__SparseEqual_" + member.Id + "(before, after);"
        );
        code.AppendLineAt(
            4,
            "return new "
                + trans
                + "(before, after, new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(__comparer), new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(__comparer), new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + editedType
                + ">(__comparer), new global::System.Collections.Generic.List<"
                + trans
                + ".Item>(), __eq);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "if (!__beforeHas || !__afterHas)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "var __added = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(__comparer);"
        );
        code.AppendLineAt(
            4,
            "if (__afterHas) foreach (var __kv in after.Value!) __added[__kv.Key] = __kv.Value;"
        );
        code.AppendLineAt(
            4,
            "var __removed = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(__comparer);"
        );
        code.AppendLineAt(
            4,
            "if (__beforeHas) foreach (var __kv in before.Value!) __removed[__kv.Key] = __kv.Value;"
        );
        code.AppendLineAt(
            4,
            "var __items = new global::System.Collections.Generic.List<" + trans + ".Item>();"
        );
        if (hasPatch)
        {
            code.AppendLineAt(
                4,
                "foreach (var __kv in __added) { "
                    + optValue
                    + " __a = "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value); "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?> __ea = "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__kv.Value!)); var __edit = "
                    + valueCs
                    + ".Between(default, __ea); __items.Add(new "
                    + trans
                    + ".Item(__kv.Key, default, __a, true, false, false, __edit, false)); }"
            );
            code.AppendLineAt(
                4,
                "foreach (var __kv in __removed) { "
                    + optValue
                    + " __b = "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value); "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?> __eb = "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__kv.Value!)); var __edit = "
                    + valueCs
                    + ".Between(__eb, default); __items.Add(new "
                    + trans
                    + ".Item(__kv.Key, __b, default, false, true, false, __edit, false)); }"
            );
        }
        else
        {
            code.AppendLineAt(
                4,
                "foreach (var __kv in __added) __items.Add(new "
                    + trans
                    + ".Item(__kv.Key, default, "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value), true, false, false, false));"
            );
            code.AppendLineAt(
                4,
                "foreach (var __kv in __removed) __items.Add(new "
                    + trans
                    + ".Item(__kv.Key, "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value), default, false, true, false, false));"
            );
        }
        code.AppendLineAt(
            4,
            "return new "
                + trans
                + "(before, after, __added, __removed, new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + editedType
                + ">(__comparer), __items, false);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "var __beforeDict = before.Value is global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + "> __bd && global::System.Object.Equals(__bd.Comparer, __comparer) ? __bd : new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">((global::System.Collections.Generic.IDictionary<"
                + keyType
                + ", "
                + valueType
                + ">)before.Value!, __comparer);"
        );
        code.AppendLineAt(
            3,
            "var __afterDict = after.Value is global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + "> __ad && global::System.Object.Equals(__ad.Comparer, __comparer) ? __ad : new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">((global::System.Collections.Generic.IDictionary<"
                + keyType
                + ", "
                + valueType
                + ">)after.Value!, __comparer);"
        );
        code.AppendLineAt(
            3,
            "var __added2 = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(__comparer);"
        );
        code.AppendLineAt(
            3,
            "var __removed2 = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(__comparer);"
        );
        code.AppendLineAt(
            3,
            "foreach (var __k in __beforeDict.Keys) if (!__afterDict.ContainsKey(__k)) __removed2[__k] = __beforeDict[__k];"
        );
        if (hasPatch)
        {
            code.AppendLineAt(
                3,
                "var __edited = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueCs
                    + ">(__comparer);"
            );
            code.AppendLineAt(3, "foreach (var __kv in __afterDict)");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "if (!__beforeDict.TryGetValue(__kv.Key, out var __b)) { __added2[__kv.Key] = __kv.Value; continue; }"
            );
            code.AppendLineAt(
                4,
                "var __nested = "
                    + valueCs
                    + ".Between("
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__b!)), "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__kv.Value)));"
            );
            code.AppendLineAt(4, "if (!__nested.IsEmpty) __edited[__kv.Key] = __nested;");
            code.AppendLineAt(3, "}");
            code.AppendLineAt(
                3,
                "bool __empty = __added2.Count == 0 && __removed2.Count == 0 && __edited.Count == 0;"
            );
            code.AppendLineAt(
                3,
                "var __items2 = new global::System.Collections.Generic.List<" + trans + ".Item>();"
            );
            code.AppendLineAt(
                3,
                "foreach (var __kv in __added2) { var __a = "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value); "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?> __ea = "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__kv.Value!)); var __edit = "
                    + valueCs
                    + ".Between(default, __ea); __items2.Add(new "
                    + trans
                    + ".Item(__kv.Key, default, __a, true, false, false, __edit, false)); }"
            );
            code.AppendLineAt(
                3,
                "foreach (var __kv in __removed2) { var __b = "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value); "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?> __eb = "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__kv.Value!)); var __edit = "
                    + valueCs
                    + ".Between(__eb, default); __items2.Add(new "
                    + trans
                    + ".Item(__kv.Key, __b, default, false, true, false, __edit, false)); }"
            );
            code.AppendLineAt(
                3,
                "foreach (var __kv in __edited) { var __b = "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__beforeDict[__kv.Key]); var __a = "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__afterDict[__kv.Key]); __items2.Add(new "
                    + trans
                    + ".Item(__kv.Key, __b, __a, false, false, true, __kv.Value, false)); }"
            );
            code.AppendLineAt(
                3,
                "return new "
                    + trans
                    + "(before, after, __added2, __removed2, __edited, __items2, __empty);"
            );
        }
        else
        {
            code.AppendLineAt(
                3,
                "var __edited = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">(__comparer);"
            );
            code.AppendLineAt(3, "foreach (var __kv in __afterDict)");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "if (!__beforeDict.TryGetValue(__kv.Key, out var __b)) { __added2[__kv.Key] = __kv.Value; continue; }"
            );
            code.AppendLineAt(
                4,
                "if (!"
                    + facade
                    + ".AreEqual((object?)__b, (object?)__kv.Value)) __edited[__kv.Key] = __kv.Value;"
            );
            code.AppendLineAt(3, "}");
            code.AppendLineAt(
                3,
                "bool __empty = __added2.Count == 0 && __removed2.Count == 0 && __edited.Count == 0;"
            );
            code.AppendLineAt(
                3,
                "var __items2 = new global::System.Collections.Generic.List<" + trans + ".Item>();"
            );
            code.AppendLineAt(
                3,
                "foreach (var __kv in __added2) __items2.Add(new "
                    + trans
                    + ".Item(__kv.Key, default, "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value), true, false, false, false));"
            );
            code.AppendLineAt(
                3,
                "foreach (var __kv in __removed2) __items2.Add(new "
                    + trans
                    + ".Item(__kv.Key, "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value), default, false, true, false, false));"
            );
            code.AppendLineAt(
                3,
                "foreach (var __kv in __edited) __items2.Add(new "
                    + trans
                    + ".Item(__kv.Key, "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__beforeDict[__kv.Key]), "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value), false, false, true, false));"
            );
            code.AppendLineAt(
                3,
                "return new "
                    + trans
                    + "(before, after, __added2, __removed2, __edited, __items2, __empty);"
            );
        }
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Gets the typed dictionary transition for member '"
                + member.Property.Name
                + "'.</summary>"
        );
        code.AppendLineAt(2, "[global::System.Text.Json.Serialization.JsonIgnore]");
        code.AppendLineAt(
            2,
            "public "
                + trans
                + " "
                + prop
                + " => __SparseBuild_"
                + member.Id
                + "(__SparseBefore_"
                + member.Id
                + "(), __SparseAfter_"
                + member.Id
                + "());"
        );
    }

    private static string KeyTypeOf(SparseMemberModel m)
    {
        if (IsDict(m))
            return m.Collection.ElementType.Name;
        return m.Collection.KeyTypeName ?? "object?";
    }

    private static string ElementTypeOf(SparseMemberModel m) => m.Collection.ElementType.Name;

    private static string ValueTypeOf(SparseMemberModel m) =>
        m.Collection.ValueType?.Name ?? "object?";

    private static string ElementChangeSetOf(SparseMemberModel m) =>
        m.Collection.ElementType.NonNullableName + ".ChangeSet";

    private static string ElementFragmentOf(SparseMemberModel m) =>
        m.Collection.ElementType.NonNullableName + ".Fragment";

    private static string ValueChangeSetOf(SparseMemberModel m) =>
        m.Collection.ValueType!.Value.NonNullableName + ".ChangeSet";

    private static string ValueFragmentOf(SparseMemberModel m) =>
        m.Collection.ValueType!.Value.NonNullableName + ".Fragment";
}
