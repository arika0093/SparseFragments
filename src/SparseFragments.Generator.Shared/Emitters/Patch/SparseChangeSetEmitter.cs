using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits the immutable baseline-aware ChangeSet sibling for a generated model (issue #85, #96, #97).</summary>
/// <remarks>
/// Issue #96: canonical storage is sparse per-path transition state, never full
/// before/after snapshots. Unchanged members retain nothing (Missing + has=false
/// or null nested). Between/FromPatch normalize to this sparse form; ToPatch/Invert
/// project from it; typed surface reads it; STJ serializes only changed paths.
/// Issue #97: Compose and Rebase operate directly on that sparse transition state.
/// Memberwise compose checks semantic contiguity only where transitions overlap
/// (scalar via generated equality, keyed/dict via canonical collection Between,
/// nested recursively); disjoint paths compose without full-state equality.
/// Whole-root transitions compose with memberwise ones through shared-baseline
/// algebra (contiguity helpers plus memberwise patch application onto the retained
/// whole endpoint, then Between). Memberwise rebase consumes per-member
/// before/desired plus the supplied current member only: scalar via equality,
/// nested recursively, keyed/dict via the canonical member-local collection rebase,
/// custom strategies via TryRebase, and Append/SetUnion members via merge-aware
/// TryRebase rather than plain equality. Keyed/dict changed members retain their
/// member-level before/after values (still sparse at member granularity: only
/// changed members are retained); composition and rebase reason over them with
/// stable-key canonical semantics rather than positional flattening.
/// </remarks>
internal static class SparseChangeSetEmitter
{
    public static void AppendChangeSet(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members
    ) => AppendChangeSet(code, members, SparseFragmentPatchEmitter.StandaloneDialect());

    public static void AppendChangeSet(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
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
        AppendFields(code, members, runtime, optionalFragment, dialect);
        AppendConstructor(code, members, runtime, optionalFragment, dialect);
        AppendIsEmpty(code, members);
        AppendBetween(code, members, runtime, optionalFragment, dialect);
        AppendFromPatch(code, members, runtime, optionalFragment);
        AppendToPatch(code, members, runtime, prefix, dialect);
        AppendInvert(code, members, runtime, optionalFragment);
        AppendCompose(code, members, runtime, optionalFragment, dialect);
        AppendMatchHelpers(code, members, runtime, optionalFragment, dialect);
        AppendRebase(
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
        AppendTypedSurface(code, members, dialect);
        SparsePatchStjEmitter.AppendChangeSetStj(code, members, dialect);
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

    private static string ChildChangeSet(
        SparseMemberModel m,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    ) => dialect.ChildChangeSetName(m);

    private static string BeforeField(SparseMemberModel m) => "__sparse_before_" + m.Id;

    private static string AfterField(SparseMemberModel m) => "__sparse_after_" + m.Id;

    private static string HasField(SparseMemberModel m) => "__sparse_has_" + m.Id;

    private static string NestedField(SparseMemberModel m) => "__sparse_nested_" + m.Id;

    // Issue #103: sparse keyed/dictionary canonical storage field names.
    // Changed keyed/dict members retain only the semantic transition
    // (whole presence transition OR granular per-key items + key-only orders),
    // never complete before/after member snapshots.
    private static string KeyedWholeFlag(SparseMemberModel m) => "__sparse_kwhole_" + m.Id;

    private static string KeyedWholeBefore(SparseMemberModel m) => "__sparse_kwholeBefore_" + m.Id;

    private static string KeyedWholeAfter(SparseMemberModel m) => "__sparse_kwholeAfter_" + m.Id;

    private static string KeyedItems(SparseMemberModel m) => "__sparse_kitems_" + m.Id;

    private static string KeyedBeforeOrder(SparseMemberModel m) => "__sparse_kbeforeOrder_" + m.Id;

    private static string KeyedAfterOrder(SparseMemberModel m) => "__sparse_kafterOrder_" + m.Id;

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
            if (IsNested(member))
                continue;
            var prefix = new System.Text.StringBuilder();
            while (usedTypes.Contains(prefix.ToString() + propNames[member.Id] + "Transition"))
                prefix.Append("Sparse");
            var t = prefix.ToString() + propNames[member.Id] + "Transition";
            usedTypes.Add(t);
            transNames[member.Id] = t;
        }
    }

    private static void AppendFields(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        code.AppendLineAt(2, "private readonly bool __sparse_hasWhole;");
        code.AppendLineAt(2, "private readonly " + optionalFragment + " __sparse_wholeBefore;");
        code.AppendLineAt(2, "private readonly " + optionalFragment + " __sparse_wholeAfter;");
        ComputePublicNames(members, out _, out var transNames);
        foreach (var member in members)
        {
            if (IsNested(member))
            {
                code.AppendLineAt(
                    2,
                    "private readonly "
                        + ChildChangeSet(member, dialect)
                        + "? "
                        + NestedField(member)
                        + ";"
                );
            }
            else if (IsKeyed(member) || IsDict(member))
            {
                // Issue #103: canonical sparse storage. No full member snapshots.
                var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
                var trans = transNames[member.Id];
                var keyType = KeyTypeOf(member);
                code.AppendLineAt(2, "private readonly bool " + HasField(member) + ";");
                code.AppendLineAt(2, "private readonly bool " + KeyedWholeFlag(member) + ";");
                code.AppendLineAt(
                    2,
                    "private readonly " + opt + " " + KeyedWholeBefore(member) + ";"
                );
                code.AppendLineAt(
                    2,
                    "private readonly " + opt + " " + KeyedWholeAfter(member) + ";"
                );
                code.AppendLineAt(
                    2,
                    "private readonly global::System.Collections.Generic.List<"
                        + trans
                        + ".Item>? "
                        + KeyedItems(member)
                        + ";"
                );
                if (IsKeyed(member))
                {
                    code.AppendLineAt(
                        2,
                        "private readonly global::System.Collections.Generic.List<"
                            + keyType
                            + ">? "
                            + KeyedBeforeOrder(member)
                            + ";"
                    );
                    code.AppendLineAt(
                        2,
                        "private readonly global::System.Collections.Generic.List<"
                            + keyType
                            + ">? "
                            + KeyedAfterOrder(member)
                            + ";"
                    );
                }
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
        string optionalFragment,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        ComputePublicNames(members, out _, out var transNames);
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
                parts.Add(ChildChangeSet(member, dialect) + "? nested" + member.Id);
            }
            else if (IsKeyed(member) || IsDict(member))
            {
                var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
                var trans = transNames[member.Id];
                var keyType = KeyTypeOf(member);
                parts.Add("bool has" + member.Id);
                parts.Add("bool whole" + member.Id);
                parts.Add(opt + " wholeBefore" + member.Id);
                parts.Add(opt + " wholeAfter" + member.Id);
                parts.Add(
                    "global::System.Collections.Generic.List<" + trans + ".Item>? items" + member.Id
                );
                if (IsKeyed(member))
                {
                    parts.Add(
                        "global::System.Collections.Generic.List<"
                            + keyType
                            + ">? beforeOrder"
                            + member.Id
                    );
                    parts.Add(
                        "global::System.Collections.Generic.List<"
                            + keyType
                            + ">? afterOrder"
                            + member.Id
                    );
                }
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
            else if (IsKeyed(member) || IsDict(member))
            {
                code.AppendLineAt(3, HasField(member) + " = has" + member.Id + ";");
                code.AppendLineAt(3, KeyedWholeFlag(member) + " = whole" + member.Id + ";");
                code.AppendLineAt(3, KeyedWholeBefore(member) + " = wholeBefore" + member.Id + ";");
                code.AppendLineAt(3, KeyedWholeAfter(member) + " = wholeAfter" + member.Id + ";");
                code.AppendLineAt(3, KeyedItems(member) + " = items" + member.Id + ";");
                if (IsKeyed(member))
                {
                    code.AppendLineAt(
                        3,
                        KeyedBeforeOrder(member) + " = beforeOrder" + member.Id + ";"
                    );
                    code.AppendLineAt(
                        3,
                        KeyedAfterOrder(member) + " = afterOrder" + member.Id + ";"
                    );
                }
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
            else if (IsKeyed(member) || IsDict(member))
            {
                parts.Add("false");
                parts.Add("false");
                parts.AddRange(new[] { "default", "default", "null" });
                if (IsKeyed(member))
                    parts.AddRange(new[] { "null", "null" });
            }
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
        string optionalFragment,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
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

    private static string MemberEmptyTail(ImmutableArray<SparseMemberModel> members)
    {
        if (members.IsDefaultOrEmpty || members.Length == 0)
            return string.Empty.Trim();
        var parts = new List<string>();
        foreach (var member in members)
        {
            if (IsNested(member))
                parts.Add("null");
            else if (IsKeyed(member) || IsDict(member))
            {
                parts.AddRange(new[] { "false", "false", "default", "default", "null" });
                if (IsKeyed(member))
                    parts.AddRange(new[] { "null", "null" });
            }
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
        string prefix,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        _ = dialect;
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
                AppendKeyedDictToPatch(code, member, name, dialect);
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
        // Per-key item inverters (issue #103): reverse add/remove, nested edits,
        // endpoints, and indexes without recovering full snapshots.
        foreach (var member in members)
        {
            if (!IsKeyed(member) && !IsDict(member))
                continue;
            AppendItemInverter(code, members, member, runtime);
        }
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
            else if (IsKeyed(member))
            {
                args.AddRange(
                    new[]
                    {
                        HasField(member),
                        KeyedWholeFlag(member),
                        KeyedWholeAfter(member),
                        KeyedWholeBefore(member),
                        "__SparseInvertItems_" + member.Id + "(" + KeyedItems(member) + ")",
                        KeyedAfterOrder(member),
                        KeyedBeforeOrder(member),
                    }
                );
            }
            else if (IsDict(member))
            {
                args.AddRange(
                    new[]
                    {
                        HasField(member),
                        KeyedWholeFlag(member),
                        KeyedWholeAfter(member),
                        KeyedWholeBefore(member),
                        "__SparseInvertItems_" + member.Id + "(" + KeyedItems(member) + ")",
                    }
                );
            }
            else
                args.AddRange(new[] { AfterField(member), BeforeField(member), HasField(member) });
        }
        code.AppendLineAt(3, "return new ChangeSet(" + string.Join(", ", args) + ");");
        code.AppendLineAt(2, "}");
    }

    private static string TransNameFor(
        ImmutableArray<SparseMemberModel> members,
        SparseMemberModel member
    )
    {
        ComputePublicNames(members, out _, out var transNames);
        return transNames[member.Id];
    }

    /// <summary>Emits the per-key item inverter for a sparse keyed/dict member.</summary>
    private static void AppendItemInverter(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseMemberModel member,
        string runtime
    )
    {
        _ = runtime;
        var trans = TransNameFor(members, member);
        var id = member.Id;
        code.AppendLineAt(
            2,
            "private static global::System.Collections.Generic.List<"
                + trans
                + ".Item>? __SparseInvertItems_"
                + id
                + "(global::System.Collections.Generic.List<"
                + trans
                + ".Item>? items)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (items is null) return null;");
        code.AppendLineAt(
            3,
            "var __out = new global::System.Collections.Generic.List<"
                + trans
                + ".Item>(items.Count);"
        );
        code.AppendLineAt(3, "foreach (var __it in items)");
        code.AppendLineAt(3, "{");
        if (IsKeyed(member))
        {
            code.AppendLineAt(
                4,
                "__out.Add(new "
                    + trans
                    + ".Item(__it.Key, __it.After, __it.Before, __it.AfterIndex, __it.BeforeIndex, __it.IsRemoved, __it.IsAdded, __it.IsEdited, __it.IsReordered, __it.Edit.Invert(), false));"
            );
        }
        else
        {
            var hasPatch = member.Collection.ValueType?.IsFragmentModel == true;
            if (hasPatch)
                code.AppendLineAt(
                    4,
                    "__out.Add(new "
                        + trans
                        + ".Item(__it.Key, __it.After, __it.Before, __it.IsRemoved, __it.IsAdded, __it.IsEdited, __it.Edit.Invert(), false));"
                );
            else
                code.AppendLineAt(
                    4,
                    "__out.Add(new "
                        + trans
                        + ".Item(__it.Key, __it.After, __it.Before, __it.IsRemoved, __it.IsAdded, __it.IsEdited, false));"
                );
        }
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return __out;");
        code.AppendLineAt(2, "}");
    }

    /// <summary>Emits ToPatch projection for a sparse keyed/dict member (issue #103).</summary>
    private static void AppendKeyedDictToPatch(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string escName,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var id = member.Id;
        var __facade = dialect.RuntimeFacade;
        var __keyType = KeyTypeOf(member);
        var coll = "Patch." + SparseFragmentPatchEmitter.CollectionPatch(member);
        var isKeyed = IsKeyed(member);
        var hasValuePatch = isKeyed
            ? member.Collection.ElementType.IsFragmentModel
            : member.Collection.ValueType?.IsFragmentModel == true;
        code.AppendLineAt(3, "if (" + HasField(member) + ")");
        code.AppendLineAt(3, "{");
        // Whole presence/null transitions project via canonical collection Between.
        code.AppendLineAt(4, "if (" + KeyedWholeFlag(member) + ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "patch."
                + escName
                + " = "
                + coll
                + ".Between("
                + KeyedWholeBefore(member)
                + ", "
                + KeyedWholeAfter(member)
                + ");"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "var __coll" + id + " = new " + coll + "();");
        code.AppendLineAt(
            5,
            "if ("
                + KeyedItems(member)
                + " is not null) foreach (var __it in "
                + KeyedItems(member)
                + ")"
        );
        code.AppendLineAt(5, "{");
        if (isKeyed)
            code.AppendLineAt(6, "if (__it.IsAdded) __coll" + id + ".Add(__it.After.Value!);");
        else
            code.AppendLineAt(
                6,
                "if (__it.IsAdded) __coll" + id + ".SetEntry(__it.Key!, __it.After.Value!);"
            );
        code.AppendLineAt(6, "else if (__it.IsRemoved)");
        if (isKeyed)
            code.AppendLineAt(6, "{ __coll" + id + ".Remove(__it.Key); }");
        else
            code.AppendLineAt(6, "{ __coll" + id + ".RemoveEntry(__it.Key!); }");
        code.AppendLineAt(6, "else if (__it.IsEdited)");
        code.AppendLineAt(6, "{");
        if (hasValuePatch)
        {
            code.AppendLineAt(
                7,
                "__coll" + id + ".__SparseSetEdited(__it.Key, __it.Edit.ToPatch());"
            );
        }
        else if (isKeyed)
        {
            code.AppendLineAt(7, "__coll" + id + ".Update(__it.After.Value!);");
        }
        else
        {
            code.AppendLineAt(7, "__coll" + id + ".UpdateEntry(__it.Key!, __it.After.Value!);");
        }
        code.AppendLineAt(6, "}");
        // Reorder-only items contribute no add/remove/edit; order is applied below.
        code.AppendLineAt(5, "}");
        if (isKeyed)
        {
            // Full key orders are always retained for granular transitions; project
            // an order patch only when the order semantically changed.
            code.AppendLineAt(
                5,
                "if ("
                    + KeyedBeforeOrder(member)
                    + " is not null && "
                    + KeyedAfterOrder(member)
                    + " is not null && !"
                    + __facade
                    + ".KeyOrderEquals<"
                    + __keyType
                    + ">("
                    + KeyedBeforeOrder(member)
                    + ", "
                    + KeyedAfterOrder(member)
                    + ")) __coll"
                    + id
                    + ".SetOrder("
                    + KeyedAfterOrder(member)
                    + ");"
            );
        }
        code.AppendLineAt(5, "patch." + escName + " = __coll" + id + ";");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
    }

    private static void AppendCompose(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        _ = optionalFragment;
        code.AppendLineAt(
            2,
            "/// <summary>Composes sequential transitions; overlapping paths must be semantically contiguous.</summary>"
        );
        code.AppendLineAt(2, "public ChangeSet Compose(ChangeSet next)");
        code.AppendLineAt(2, "{");
        var __hasSparse = members.Any(static m => IsKeyed(m) || IsDict(m));
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
        // Shared-baseline algebra (issue #97): a whole-root endpoint composes with a
        // memberwise transition when the memberwise side is contiguous with the retained
        // whole state. Only overlapping (changed) paths are checked; disjoint paths
        // compose without full-state equality. The merged endpoint is derived by
        // applying the memberwise patch onto the retained whole state, then Between
        // normalizes (including back to no-op where before == final after).
        code.AppendLineAt(4, "if (__sparse_hasWhole)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (!next.__SparseBeforeMatches(__sparse_wholeAfter)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(5, "var __mergedAfter = next.ToPatch().Apply(__sparse_wholeAfter);");
        code.AppendLineAt(5, "return Between(__sparse_wholeBefore, __mergedAfter);");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "if (!__SparseAfterMatches(next.__sparse_wholeBefore)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(
            4,
            "var __mergedBefore = Invert().ToPatch().Apply(next.__sparse_wholeBefore);"
        );
        code.AppendLineAt(4, "return Between(__mergedBefore, next.__sparse_wholeAfter);");
        code.AppendLineAt(3, "}");
        if (__hasSparse)
            AppendPragmaDisableNullKey(code, 3);
        foreach (var member in members)
        {
            if (!IsNested(member))
                continue;
            var child = ChildChangeSet(member, dialect);
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
        // Scalar members keep snapshot equality; keyed/dict compose per interacting
        // key (issue #103) so disjoint keys compose despite differing unrelated snapshots.
        foreach (var member in members)
        {
            if (IsNested(member))
                continue;
            if (IsKeyed(member))
            {
                AppendKeyedComposeSparse(code, members, member, runtime, dialect);
                continue;
            }
            if (IsDict(member))
            {
                AppendDictComposeSparse(code, members, member, runtime, dialect);
                continue;
            }
            var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
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
            else if (IsKeyed(member))
                args.AddRange(
                    new[]
                    {
                        "__cb" + member.Id + "_has",
                        "__cb" + member.Id + "_whole",
                        "__cb" + member.Id + "_wb",
                        "__cb" + member.Id + "_wa",
                        "__cb" + member.Id + "_items",
                        "__cb" + member.Id + "_bO",
                        "__cb" + member.Id + "_aO",
                    }
                );
            else if (IsDict(member))
                args.AddRange(
                    new[]
                    {
                        "__cb" + member.Id + "_has",
                        "__cb" + member.Id + "_whole",
                        "__cb" + member.Id + "_wb",
                        "__cb" + member.Id + "_wa",
                        "__cb" + member.Id + "_items",
                    }
                );
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
        if (__hasSparse)
            AppendPragmaRestoreNullKey(code, 3);
        code.AppendLineAt(3, "return new ChangeSet(" + string.Join(", ", args) + ");");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(2, "/// <summary>Composes two sequential change sets.</summary>");
        code.AppendLineAt(2, "public static ChangeSet Compose(ChangeSet first, ChangeSet second)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (first is null) throw new global::System.ArgumentNullException(nameof(first));"
        );
        code.AppendLineAt(3, "return first." + "Compose(second);");
        code.AppendLineAt(2, "}");
    }

    /// <summary>Emits per-key sparse compose for a keyed member (issue #103).</summary>
    /// <remarks>
    /// Declares locals __cb_has/__cb_whole/__cb_wb/__cb_wa/__cb_items/__cb_bO/__cb_aO.
    /// Disjoint keys compose without full-snapshot equality; overlapping keys require
    /// semantic contiguity (added/removed/edited continuity via element equality and
    /// nested ChangeSet compose). Net no-ops normalize to empty. Whole member
    /// transitions fall back to full-endpoint composition.
    /// </remarks>
    private static void AppendPragmaDisableNullKey(SharedIndentedBuilder code, int indent)
    {
        // Sparse keyed/dictionary transitions flow stable keys through per-key maps
        // across all key shapes (string/int/tuple/interface keys). The keys are
        // non-null by construction (validated during Between/STJ read), but nullable
        // analysis cannot prove it uniformly: `!` is invalid on value-type keys
        // (CS8715) while omitting it warns on reference-type keys (CS8604).
        code.AppendLineAt(
            indent,
            "#pragma warning disable CS8604 // Sparse stable keys are non-null by construction."
        );
    }

    private static void AppendPragmaRestoreNullKey(SharedIndentedBuilder code, int indent)
    {
        code.AppendLineAt(indent, "#pragma warning restore CS8604");
    }

    private static void AppendKeyedComposeSparse(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseMemberModel member,
        string runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var id = member.Id;
        var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
        var keyType = KeyTypeOf(member);
        var elementCs = ElementChangeSetOf(member);
        var elementFrag = ElementFragmentOf(member);
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var facade = dialect.RuntimeFacade;
        var trans = TransNameFor(members, member);
        code.AppendLineAt(3, "bool __cb" + id + "_has = false;");
        code.AppendLineAt(3, "bool __cb" + id + "_whole = false;");
        code.AppendLineAt(3, opt + " __cb" + id + "_wb = default;");
        code.AppendLineAt(3, opt + " __cb" + id + "_wa = default;");
        code.AppendLineAt(
            3,
            "global::System.Collections.Generic.List<"
                + trans
                + ".Item>? __cb"
                + id
                + "_items = null;"
        );
        code.AppendLineAt(
            3,
            "global::System.Collections.Generic.List<" + keyType + ">? __cb" + id + "_bO = null;"
        );
        code.AppendLineAt(
            3,
            "global::System.Collections.Generic.List<" + keyType + ">? __cb" + id + "_aO = null;"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var __first" + id + "_has = " + HasField(member) + ";");
        code.AppendLineAt(4, "var __second" + id + "_has = next." + HasField(member) + ";");
        code.AppendLineAt(4, "if (!__first" + id + "_has)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "__cb"
                + id
                + "_has = __second"
                + id
                + "_has; __cb"
                + id
                + "_whole = next."
                + KeyedWholeFlag(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "__cb"
                + id
                + "_wb = next."
                + KeyedWholeBefore(member)
                + "; __cb"
                + id
                + "_wa = next."
                + KeyedWholeAfter(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "__cb"
                + id
                + "_items = next."
                + KeyedItems(member)
                + "; __cb"
                + id
                + "_bO = next."
                + KeyedBeforeOrder(member)
                + "; __cb"
                + id
                + "_aO = next."
                + KeyedAfterOrder(member)
                + ";"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else if (!__second" + id + "_has)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "__cb" + id + "_has = true; __cb" + id + "_whole = " + KeyedWholeFlag(member) + ";"
        );
        code.AppendLineAt(
            5,
            "__cb"
                + id
                + "_wb = "
                + KeyedWholeBefore(member)
                + "; __cb"
                + id
                + "_wa = "
                + KeyedWholeAfter(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "__cb"
                + id
                + "_items = "
                + KeyedItems(member)
                + "; __cb"
                + id
                + "_bO = "
                + KeyedBeforeOrder(member)
                + "; __cb"
                + id
                + "_aO = "
                + KeyedAfterOrder(member)
                + ";"
        );
        code.AppendLineAt(4, "}");
        // Whole member transitions (Missing/null/value): endpoint composition.
        code.AppendLineAt(
            4,
            "else if (" + KeyedWholeFlag(member) + " || next." + KeyedWholeFlag(member) + ")"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "__cb" + id + "_has = true;");
        code.AppendLineAt(
            5,
            "var __w1b = "
                + KeyedWholeBefore(member)
                + "; var __w1a = "
                + KeyedWholeAfter(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "var __w2b = next."
                + KeyedWholeBefore(member)
                + "; var __w2a = next."
                + KeyedWholeAfter(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "if (" + KeyedWholeFlag(member) + " && next." + KeyedWholeFlag(member) + ")"
        );
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (!Fragment.__SparseEqual_"
                + id
                + "(__w1a, __w2b)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(
            6,
            "if (Fragment.__SparseEqual_" + id + "(__w1b, __w2a)) { __cb" + id + "_has = false; }"
        );
        code.AppendLineAt(
            6,
            "else { __cb"
                + id
                + "_whole = true; __cb"
                + id
                + "_wb = __w1b; __cb"
                + id
                + "_wa = __w2a; }"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        // Granular + granular per-key merge.
        code.AppendLineAt(
            5,
            "var __map1"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + trans
                + ".Item>("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            5,
            "if ("
                + KeyedItems(member)
                + " is not null) foreach (var __it in "
                + KeyedItems(member)
                + ") __map1"
                + id
                + "[__it.Key] = __it;"
        );
        code.AppendLineAt(
            5,
            "var __map2"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + trans
                + ".Item>("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            5,
            "if (next."
                + KeyedItems(member)
                + " is not null) foreach (var __it in next."
                + KeyedItems(member)
                + ") __map2"
                + id
                + "[__it.Key] = __it;"
        );
        code.AppendLineAt(
            5,
            "var __net"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + trans
                + ".Item>("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            5,
            "var __keys"
                + id
                + " = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(__map1"
                + id
                + ".Keys, "
                + comparer
                + ");"
        );
        code.AppendLineAt(5, "__keys" + id + ".UnionWith(__map2" + id + ".Keys);");
        code.AppendLineAt(5, "foreach (var __k in __keys" + id + ")");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "var __has1 = __map1"
                + id
                + ".TryGetValue(__k, out var __a1); var __has2 = __map2"
                + id
                + ".TryGetValue(__k, out var __a2);"
        );
        // Directional continuity (issue #103): second's expected before must hold in
        // first's after. First-only keys need no check (second adapts); second-only
        // keys validate presence against first's retained key orders so disjoint
        // edits compose while remove/edit of absent keys still fail.
        code.AppendLineAt(6, "if (!__has1)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(7, "var __o1aChk = " + KeyedAfterOrder(member) + ";");
        code.AppendLineAt(
            7,
            "if (__o1aChk is not null) { bool __in1 = false; foreach (var __ok in __o1aChk) if ("
                + comparer
                + ".Equals(__ok, __k)) { __in1 = true; break; } if (__a2!.IsAdded ? __in1 : !__in1) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\"); }"
        );
        code.AppendLineAt(7, "__net" + id + "[__k] = __a2!; continue;");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "if (!__has2) { __net" + id + "[__k] = __a1!; continue; }");
        // Both changed: validate continuity and merge by membership kind.
        // Added (Missing->after) cases.
        code.AppendLineAt(6, "if (__a1!.IsAdded && __a2!.IsAdded)");
        code.AppendLineAt(
            6,
            "{ throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\"); }"
        );
        code.AppendLineAt(6, "if (__a1!.IsAdded && __a2!.IsRemoved)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (!"
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a1.After.Value!)), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a2.Before.Value!))).IsEmpty) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(7, "continue;");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "if (__a1!.IsRemoved && __a2!.IsAdded)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if ("
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a1.Before.Value!)), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a2.After.Value!))).IsEmpty) continue;"
        );
        code.AppendLineAt(
            7,
            runtime
                + "Optional<"
                + elementFrag
                + "?> __eb = "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a1.Before.Value!));"
        );
        code.AppendLineAt(
            7,
            runtime
                + "Optional<"
                + elementFrag
                + "?> __ea = "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a2.After.Value!));"
        );
        code.AppendLineAt(7, "var __edit = " + elementCs + ".Between(__eb, __ea);");
        code.AppendLineAt(
            7,
            "__net"
                + id
                + "[__k] = new "
                + trans
                + ".Item(__k, __a1.Before, __a2.After, __a1.BeforeIndex, __a2.AfterIndex, false, false, true, false, __edit, false);"
        );
        code.AppendLineAt(6, "continue;");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "if (__a1!.IsRemoved || __a2!.IsAdded)");
        code.AppendLineAt(
            6,
            "{ throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\"); }"
        );
        // Added then edited: continuity first.After == second.Before.
        code.AppendLineAt(6, "if (__a1!.IsAdded && __a2!.IsEdited)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (!"
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a1.After.Value!)), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a2.Before.Value!))).IsEmpty) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(
            7,
            runtime
                + "Optional<"
                + elementFrag
                + "?> __ea2 = "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a2.After.Value!));"
        );
        code.AppendLineAt(7, "var __edit2 = " + elementCs + ".Between(default, __ea2);");
        code.AppendLineAt(
            7,
            "__net"
                + id
                + "[__k] = new "
                + trans
                + ".Item(__k, default, __a2.After, -1, __a2.AfterIndex, true, false, false, false, __edit2, false);"
        );
        code.AppendLineAt(6, "continue;");
        code.AppendLineAt(6, "}");
        // Edited then removed: continuity first.After == second.Before.
        code.AppendLineAt(6, "if ((__a1!.IsEdited || __a1!.IsReordered) && __a2!.IsRemoved)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (__a1.IsEdited && !"
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a1.After.Value!)), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a2.Before.Value!))).IsEmpty) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(
            7,
            "__net"
                + id
                + "[__k] = new "
                + trans
                + ".Item(__k, __a1.Before.IsPresent ? __a1.Before : __a2.Before, default, __a1.BeforeIndex >= 0 ? __a1.BeforeIndex : __a2.BeforeIndex, -1, false, true, false, false, "
                + elementCs
                + ".Between(__a1.IsEdited ? "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a1.Before.Value!)) : default, default), false);"
        );
        // Fixup Before when first was reorder-only (no Before stored? reorder-only has Before present).
        code.AppendLineAt(6, "continue;");
        code.AppendLineAt(6, "}");
        // Edited/edited (or reorder-involved) continuity via nested compose; scalar fallback via equality.
        code.AppendLineAt(6, "if (__a1!.IsEdited && __a2!.IsEdited)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(7, "var __cc = __a1.Edit.Compose(__a2.Edit);");
        code.AppendLineAt(7, "if (__cc.IsEmpty) { continue; }");
        code.AppendLineAt(
            7,
            "__net"
                + id
                + "[__k] = new "
                + trans
                + ".Item(__k, __a1.Before, __a2.After, __a1.BeforeIndex, __a2.AfterIndex, false, false, true, __a1.IsReordered || __a2.IsReordered, __cc, false);"
        );
        code.AppendLineAt(6, "continue;");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "if (__a1!.IsEdited || __a2!.IsEdited)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (__a1.IsEdited && __a2.IsReordered && !__a2.IsEdited) { __net"
                + id
                + "[__k] = new "
                + trans
                + ".Item(__k, __a1.Before, __a1.After, __a1.BeforeIndex, __a2.AfterIndex, false, false, true, true, __a1.Edit, false); continue; }"
        );
        code.AppendLineAt(
            7,
            "if (__a2.IsEdited && __a1.IsReordered && !__a1.IsEdited) { var __cc2 = __a2.Edit; __net"
                + id
                + "[__k] = new "
                + trans
                + ".Item(__k, __a2.Before, __a2.After, __a1.BeforeIndex, __a2.AfterIndex, false, false, true, true, __cc2, false); continue; }"
        );
        code.AppendLineAt(
            7,
            "throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(6, "}");
        // Reorder-only on both or one side: continuity holds (same values); net keeps reorder flag.
        code.AppendLineAt(
            6,
            "if (__a1!.IsReordered || __a2!.IsReordered) { var __keep = __a2!.IsReordered || __a1!.IsReordered; var __b = __a1.Before.IsPresent ? __a1.Before : __a2.Before; var __a = __a2.After.IsPresent ? __a2.After : __a1.After; var __bi = __a1.BeforeIndex >= 0 ? __a1.BeforeIndex : __a2.BeforeIndex; var __ai = __a2.AfterIndex >= 0 ? __a2.AfterIndex : __a1.AfterIndex; __net"
                + id
                + "[__k] = new "
                + trans
                + ".Item(__k, __b, __a, __bi, __ai, false, false, false, true, "
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__b.Value!)), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a.Value!))), false); continue; }"
        );
        code.AppendLineAt(
            6,
            "throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(5, "}");
        // Net no-op normalization is implicit (empty dict => has false below).
        // Orders: next wins when present (filtered to net keys), else first filtered (mirrors Patch order compose).
        code.AppendLineAt(
            5,
            "var __netRemoved"
                + id
                + " = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            5,
            "foreach (var __kv in __net"
                + id
                + ") if (__kv.Value.IsRemoved) __netRemoved"
                + id
                + ".Add(__kv.Key);"
        );
        code.AppendLineAt(
            5,
            "global::System.Collections.Generic.List<"
                + keyType
                + ">? __o1b = "
                + KeyedBeforeOrder(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "global::System.Collections.Generic.List<"
                + keyType
                + ">? __o1a = "
                + KeyedAfterOrder(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "global::System.Collections.Generic.List<"
                + keyType
                + ">? __o2b = next."
                + KeyedBeforeOrder(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "global::System.Collections.Generic.List<"
                + keyType
                + ">? __o2a = next."
                + KeyedAfterOrder(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "global::System.Collections.Generic.List<" + keyType + ">? __nbO" + id + " = __o1b; "
        );
        code.AppendLineAt(
            5,
            "global::System.Collections.Generic.List<" + keyType + ">? __naO" + id + " = null;"
        );
        code.AppendLineAt(5, "if (__o2a is not null)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "var __no = new global::System.Collections.Generic.List<"
                + keyType
                + ">(); foreach (var __k in __o2a) if (!__netRemoved"
                + id
                + ".Contains(__k)) __no.Add(__k);"
        );
        code.AppendLineAt(
            6,
            "foreach (var __kv in __net"
                + id
                + ") if (__kv.Value.IsAdded && !__no.Contains(__kv.Key, "
                + comparer
                + ")) __no.Add(__kv.Key);"
        );
        code.AppendLineAt(6, "__naO" + id + " = __no;");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else if (__o1a is not null)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "var __no = new global::System.Collections.Generic.List<"
                + keyType
                + ">(); foreach (var __k in __o1a) if (!__netRemoved"
                + id
                + ".Contains(__k)) __no.Add(__k);"
        );
        code.AppendLineAt(
            6,
            "foreach (var __kv in __net"
                + id
                + ") if (__kv.Value.IsAdded && !__no.Contains(__kv.Key, "
                + comparer
                + ")) __no.Add(__kv.Key);"
        );
        code.AppendLineAt(6, "__naO" + id + " = __no;");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "if (__net" + id + ".Count == 0) { }");
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "__cb" + id + "_has = true;");
        code.AppendLineAt(
            6,
            "var __elist"
                + id
                + " = new global::System.Collections.Generic.List<"
                + trans
                + ".Item>(__net"
                + id
                + ".Count);"
        );
        // Enumeration: net after-order then net removed in net before-order (mirrors Between).
        code.AppendLineAt(6, "if (__naO" + id + " is not null)");
        code.AppendLineAt(
            6,
            "{ foreach (var __k in __naO"
                + id
                + ") if (__net"
                + id
                + ".TryGetValue(__k, out var __e) && !__e.IsRemoved) __elist"
                + id
                + ".Add(__e); }"
        );
        code.AppendLineAt(
            6,
            "else foreach (var __kv in __net"
                + id
                + ") if (!__kv.Value.IsRemoved) __elist"
                + id
                + ".Add(__kv.Value);"
        );
        code.AppendLineAt(6, "if (__nbO" + id + " is not null)");
        code.AppendLineAt(
            6,
            "{ foreach (var __k in __nbO"
                + id
                + ") if (__net"
                + id
                + ".TryGetValue(__k, out var __e) && __e.IsRemoved) __elist"
                + id
                + ".Add(__e); }"
        );
        code.AppendLineAt(
            6,
            "else foreach (var __kv in __net"
                + id
                + ") if (__kv.Value.IsRemoved) __elist"
                + id
                + ".Add(__kv.Value);"
        );
        code.AppendLineAt(6, "__cb" + id + "_items = __elist" + id + ";");
        code.AppendLineAt(
            6,
            "__cb" + id + "_bO = __nbO" + id + "; __cb" + id + "_aO = __naO" + id + ";"
        );
        // Normalize order-only equality to sparse (no orders when equal).
        code.AppendLineAt(
            6,
            "if (__nbO"
                + id
                + " is not null && __naO"
                + id
                + " is not null && "
                + facade
                + ".KeyOrderEquals<"
                + keyType
                + ">(__nbO"
                + id
                + ", __naO"
                + id
                + ") && __net"
                + id
                + ".Count != 0) { bool __onlyOrder = true; foreach (var __kv in __net"
                + id
                + ") if (__kv.Value.IsAdded || __kv.Value.IsRemoved || __kv.Value.IsEdited) { __onlyOrder = false; break; } if (__onlyOrder && __elist"
                + id
                + ".Count == 0) { __cb"
                + id
                + "_has = false; __cb"
                + id
                + "_items = null; __cb"
                + id
                + "_bO = null; __cb"
                + id
                + "_aO = null; } }"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
    }

    /// <summary>Emits per-key sparse compose for a dictionary member (issue #103).</summary>
    private static void AppendDictComposeSparse(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseMemberModel member,
        string runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var id = member.Id;
        var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
        var keyType = KeyTypeOf(member);
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var facade = dialect.RuntimeFacade;
        var hasPatch = member.Collection.ValueType?.IsFragmentModel == true;
        var valueCs = hasPatch ? ValueChangeSetOf(member) : null;
        var valueFrag = hasPatch ? ValueFragmentOf(member) : null;
        var trans = TransNameFor(members, member);
        code.AppendLineAt(3, "bool __cb" + id + "_has = false;");
        code.AppendLineAt(3, "bool __cb" + id + "_whole = false;");
        code.AppendLineAt(3, opt + " __cb" + id + "_wb = default;");
        code.AppendLineAt(3, opt + " __cb" + id + "_wa = default;");
        code.AppendLineAt(
            3,
            "global::System.Collections.Generic.List<"
                + trans
                + ".Item>? __cb"
                + id
                + "_items = null;"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var __first" + id + "_has = " + HasField(member) + ";");
        code.AppendLineAt(4, "var __second" + id + "_has = next." + HasField(member) + ";");
        code.AppendLineAt(4, "if (!__first" + id + "_has)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "__cb"
                + id
                + "_has = __second"
                + id
                + "_has; __cb"
                + id
                + "_whole = next."
                + KeyedWholeFlag(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "__cb"
                + id
                + "_wb = next."
                + KeyedWholeBefore(member)
                + "; __cb"
                + id
                + "_wa = next."
                + KeyedWholeAfter(member)
                + ";"
        );
        code.AppendLineAt(5, "__cb" + id + "_items = next." + KeyedItems(member) + ";");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else if (!__second" + id + "_has)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "__cb" + id + "_has = true; __cb" + id + "_whole = " + KeyedWholeFlag(member) + ";"
        );
        code.AppendLineAt(
            5,
            "__cb"
                + id
                + "_wb = "
                + KeyedWholeBefore(member)
                + "; __cb"
                + id
                + "_wa = "
                + KeyedWholeAfter(member)
                + ";"
        );
        code.AppendLineAt(5, "__cb" + id + "_items = " + KeyedItems(member) + ";");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "else if (" + KeyedWholeFlag(member) + " || next." + KeyedWholeFlag(member) + ")"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "__cb" + id + "_has = true;");
        code.AppendLineAt(
            5,
            "var __w1b = "
                + KeyedWholeBefore(member)
                + "; var __w1a = "
                + KeyedWholeAfter(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "var __w2b = next."
                + KeyedWholeBefore(member)
                + "; var __w2a = next."
                + KeyedWholeAfter(member)
                + ";"
        );
        // Both whole: continuity on middle, net whole when non-empty.
        code.AppendLineAt(
            5,
            "if (" + KeyedWholeFlag(member) + " && next." + KeyedWholeFlag(member) + ")"
        );
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (!Fragment.__SparseEqual_"
                + id
                + "(__w1a, __w2b)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(
            6,
            "if (Fragment.__SparseEqual_" + id + "(__w1b, __w2a)) { __cb" + id + "_has = false; }"
        );
        code.AppendLineAt(
            6,
            "else { __cb"
                + id
                + "_whole = true; __cb"
                + id
                + "_wb = __w1b; __cb"
                + id
                + "_wa = __w2a; }"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
        // Mixed whole/granular member transitions are out of the sparse fast path;
        // require endpoint contiguity via the canonical patch composition.
        code.AppendLineAt(
            6,
            "throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "var __map1"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + trans
                + ".Item>("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            5,
            "if ("
                + KeyedItems(member)
                + " is not null) foreach (var __it in "
                + KeyedItems(member)
                + ") __map1"
                + id
                + "[__it.Key!] = __it;"
        );
        code.AppendLineAt(
            5,
            "var __map2"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + trans
                + ".Item>("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            5,
            "if (next."
                + KeyedItems(member)
                + " is not null) foreach (var __it in next."
                + KeyedItems(member)
                + ") __map2"
                + id
                + "[__it.Key!] = __it;"
        );
        code.AppendLineAt(
            5,
            "var __net"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + trans
                + ".Item>("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            5,
            "var __keys"
                + id
                + " = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(__map1"
                + id
                + ".Keys, "
                + comparer
                + ");"
        );
        code.AppendLineAt(5, "__keys" + id + ".UnionWith(__map2" + id + ".Keys);");
        code.AppendLineAt(5, "foreach (var __k in __keys" + id + ")");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "var __has1 = __map1"
                + id
                + ".TryGetValue(__k!, out var __a1); var __has2 = __map2"
                + id
                + ".TryGetValue(__k!, out var __a2);"
        );
        code.AppendLineAt(6, "if (!__has1) { __net" + id + "[__k!] = __a2!; continue; }");
        code.AppendLineAt(6, "if (!__has2) { __net" + id + "[__k!] = __a1!; continue; }");
        code.AppendLineAt(6, "if (__a1!.IsAdded && __a2!.IsAdded)");
        code.AppendLineAt(
            6,
            "{ throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\"); }"
        );
        code.AppendLineAt(6, "if (__a1!.IsAdded && __a2!.IsRemoved)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (!"
                + facade
                + ".AreEqual((object?)__a1.After.Value, (object?)__a2.Before.Value)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(7, "continue;");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "if (__a1!.IsRemoved && __a2!.IsAdded)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if ("
                + facade
                + ".AreEqual((object?)__a1.Before.Value, (object?)__a2.After.Value)) continue;"
        );
        if (hasPatch)
        {
            code.AppendLineAt(
                7,
                runtime
                    + "Optional<"
                    + valueFrag
                    + "?> __eb = "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__a1.Before.Value!));"
            );
            code.AppendLineAt(
                7,
                runtime
                    + "Optional<"
                    + valueFrag
                    + "?> __ea = "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__a2.After.Value!));"
            );
            code.AppendLineAt(7, "var __edit = " + valueCs + ".Between(__eb, __ea);");
            code.AppendLineAt(
                7,
                "__net"
                    + id
                    + "[__k!] = new "
                    + trans
                    + ".Item(__k!, __a1.Before, __a2.After, false, false, true, __edit, false);"
            );
        }
        else
        {
            code.AppendLineAt(
                7,
                "__net"
                    + id
                    + "[__k!] = new "
                    + trans
                    + ".Item(__k!, __a1.Before, __a2.After, false, false, true, false);"
            );
        }
        code.AppendLineAt(6, "continue;");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "if (__a1!.IsRemoved || __a2!.IsAdded)");
        code.AppendLineAt(
            6,
            "{ throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\"); }"
        );
        code.AppendLineAt(6, "if (__a1!.IsAdded && __a2!.IsEdited)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (!"
                + facade
                + ".AreEqual((object?)__a1.After.Value, (object?)__a2.Before.Value)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        if (hasPatch)
        {
            code.AppendLineAt(
                7,
                runtime
                    + "Optional<"
                    + valueFrag
                    + "?> __ea2 = "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__a2.After.Value!));"
            );
            code.AppendLineAt(7, "var __edit2 = " + valueCs + ".Between(default, __ea2);");
            code.AppendLineAt(
                7,
                "__net"
                    + id
                    + "[__k!] = new "
                    + trans
                    + ".Item(__k!, default, __a2.After, true, false, false, __edit2, false);"
            );
        }
        else
        {
            code.AppendLineAt(
                7,
                "__net"
                    + id
                    + "[__k!] = new "
                    + trans
                    + ".Item(__k!, default, __a2.After, true, false, false, false);"
            );
        }
        code.AppendLineAt(6, "continue;");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "if (__a1!.IsEdited && __a2!.IsRemoved)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (!"
                + facade
                + ".AreEqual((object?)__a1.After.Value, (object?)__a2.Before.Value)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        if (hasPatch)
        {
            code.AppendLineAt(
                7,
                runtime
                    + "Optional<"
                    + valueFrag
                    + "?> __eb1 = "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__a1.Before.Value!));"
            );
            code.AppendLineAt(7, "var __redit = " + valueCs + ".Between(__eb1, default);");
            code.AppendLineAt(
                7,
                "__net"
                    + id
                    + "[__k!] = new "
                    + trans
                    + ".Item(__k!, __a1.Before, default, false, true, false, __redit, false);"
            );
        }
        else
        {
            code.AppendLineAt(
                7,
                "__net"
                    + id
                    + "[__k!] = new "
                    + trans
                    + ".Item(__k!, __a1.Before, default, false, true, false, false);"
            );
        }
        code.AppendLineAt(6, "continue;");
        code.AppendLineAt(6, "}");
        if (hasPatch)
        {
            code.AppendLineAt(6, "if (__a1!.IsEdited && __a2!.IsEdited)");
            code.AppendLineAt(6, "{");
            code.AppendLineAt(7, "var __cc = __a1.Edit.Compose(__a2.Edit);");
            code.AppendLineAt(7, "if (__cc.IsEmpty) { continue; }");
            code.AppendLineAt(
                7,
                "__net"
                    + id
                    + "[__k!] = new "
                    + trans
                    + ".Item(__k!, __a1.Before, __a2.After, false, false, true, __cc, false);"
            );
            code.AppendLineAt(6, "continue;");
            code.AppendLineAt(6, "}");
            code.AppendLineAt(
                6,
                "throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
            );
        }
        else
        {
            code.AppendLineAt(6, "if (__a1!.IsEdited && __a2!.IsEdited)");
            code.AppendLineAt(6, "{");
            code.AppendLineAt(
                7,
                "if (!"
                    + facade
                    + ".AreEqual((object?)__a1.After.Value, (object?)__a2.Before.Value)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
            );
            code.AppendLineAt(
                7,
                "if ("
                    + facade
                    + ".AreEqual((object?)__a1.Before.Value, (object?)__a2.After.Value)) { continue; }"
            );
            code.AppendLineAt(
                7,
                "__net"
                    + id
                    + "[__k!] = new "
                    + trans
                    + ".Item(__k!, __a1.Before, __a2.After, false, false, true, false);"
            );
            code.AppendLineAt(6, "continue;");
            code.AppendLineAt(6, "}");
            code.AppendLineAt(
                6,
                "throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
            );
        }
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "if (__net" + id + ".Count == 0) { }");
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "__cb" + id + "_has = true;");
        code.AppendLineAt(
            6,
            "var __elist"
                + id
                + " = new global::System.Collections.Generic.List<"
                + trans
                + ".Item>(__net"
                + id
                + ".Count);"
        );
        code.AppendLineAt(
            6,
            "foreach (var __kv in __net" + id + ") __elist" + id + ".Add(__kv.Value);"
        );
        code.AppendLineAt(6, "__cb" + id + "_items = __elist" + id + ";");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
    }

    /// <summary>
    /// Emits the shared-baseline contiguity probes used by whole-root/memberwise composition (issue #97).
    /// </summary>
    /// <remarks>
    /// Each probe checks semantic continuity only on changed paths: an empty transition
    /// matches any state, a whole-root transition compares whole endpoints, and a
    /// memberwise transition compares per-member before (or after) values with the
    /// supplied state, recursing through nested subtrees. Keyed/dictionary members use
    /// the canonical key-aware collection Between emptiness check, mirroring memberwise
    /// compose; all other members use the generated semantic member equality.
    /// </remarks>
    private static void AppendMatchHelpers(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        _ = runtime;
        foreach (var side in new[] { "Before", "After" })
        {
            var field = side == "Before" ? "__sparse_wholeBefore" : "__sparse_wholeAfter";
            code.AppendLineAt(
                2,
                "/// <summary>Whether the supplied state matches this transition's "
                    + (side == "Before" ? "before" : "after")
                    + " on every changed path.</summary>"
            );
            code.AppendLineAt(
                2,
                "internal bool __Sparse" + side + "Matches(" + optionalFragment + " state)"
            );
            code.AppendLineAt(2, "{");
            var __hasSparseMatch = members.Any(static m => IsKeyed(m) || IsDict(m));
            code.AppendLineAt(3, "if (IsEmpty) return true;");
            code.AppendLineAt(
                3,
                "if (__sparse_hasWhole) return Fragment.__SparseAreEqual(" + field + ", state);"
            );
            code.AppendLineAt(3, "if (!state.IsPresent || state.Value is null) return false;");
            code.AppendLineAt(3, "var __st = state.Value!;");
            if (__hasSparseMatch)
                AppendPragmaDisableNullKey(code, 3);
            foreach (var member in members)
            {
                var esc = SparseNaming.EscapeIdentifier(member.Property.Name);
                if (IsNested(member))
                {
                    code.AppendLineAt(
                        3,
                        "if ("
                            + NestedField(member)
                            + " is not null && !"
                            + NestedField(member)
                            + ".__Sparse"
                            + side
                            + "Matches(__st."
                            + esc
                            + ")) return false;"
                    );
                }
                else if (IsKeyed(member) || IsDict(member))
                {
                    AppendSparseMatchMember(code, member, esc, side, runtime, dialect);
                }
                else
                {
                    var own = side == "Before" ? BeforeField(member) : AfterField(member);
                    code.AppendLineAt(3, "if (" + HasField(member) + ")");
                    code.AppendLineAt(3, "{");
                    code.AppendLineAt(
                        4,
                        "if (!Fragment.__SparseEqual_"
                            + member.Id
                            + "(__st."
                            + esc
                            + ", "
                            + own
                            + ")) return false;"
                    );
                    code.AppendLineAt(3, "}");
                }
            }
            if (__hasSparseMatch)
                AppendPragmaRestoreNullKey(code, 3);
            code.AppendLineAt(3, "return true;");
            code.AppendLineAt(2, "}");
        }
    }

    /// <summary>Emits per-key sparse contiguity probe for a keyed/dict member.</summary>
    private static void AppendSparseMatchMember(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string esc,
        string side,
        string runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var id = member.Id;
        var isKeyed = IsKeyed(member);
        var keyType = KeyTypeOf(member);
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var facade = dialect.RuntimeFacade;
        var ownWhole = side == "Before" ? KeyedWholeBefore(member) : KeyedWholeAfter(member);
        var ownOrder = side == "Before" ? KeyedBeforeOrder(member) : KeyedAfterOrder(member);
        code.AppendLineAt(3, "if (" + HasField(member) + ")");
        code.AppendLineAt(3, "{");
        // Whole presence/null transitions compare full endpoints.
        code.AppendLineAt(4, "if (" + KeyedWholeFlag(member) + ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (!Fragment.__SparseEqual_"
                + id
                + "(__st."
                + esc
                + ", "
                + ownWhole
                + ")) return false;"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(4, "var __cur" + id + " = __st." + esc + ";");
        code.AppendLineAt(
            4,
            "if (!__cur"
                + id
                + ".IsPresent || (object?)__cur"
                + id
                + ".Value is null) return false;"
        );
        if (isKeyed)
        {
            // Order interacts only where stored (orderChanged); otherwise membership only.
            code.AppendLineAt(4, "if (" + ownOrder + " is not null)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "var __curOrder"
                    + id
                    + " = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">();"
            );
            code.AppendLineAt(
                5,
                "foreach (var __e in __cur"
                    + id
                    + ".Value!) __curOrder"
                    + id
                    + ".Add(__SparseKeyOf_ChangeSet_"
                    + id
                    + "(__e));"
            );
            code.AppendLineAt(
                5,
                "if (!"
                    + facade
                    + ".KeyOrderEquals<"
                    + keyType
                    + ">(__curOrder"
                    + id
                    + ", "
                    + ownOrder
                    + ")) return false;"
            );
            code.AppendLineAt(4, "}");
        }
        // Per-key endpoint checks from stored items (no full snapshot retention).
        code.AppendLineAt(
            4,
            "if ("
                + KeyedItems(member)
                + " is not null) foreach (var __it in "
                + KeyedItems(member)
                + ")"
        );
        code.AppendLineAt(4, "{");
        if (isKeyed)
        {
            var elementFrag = ElementFragmentOf(member);
            var elementCs = ElementChangeSetOf(member);
            // Fragment-aware per-key equality via nested ChangeSet emptiness
            // (handles cloned element instances with equal values).
            // Between(a,b).IsEmpty == equal; mismatch is !IsEmpty.
            var mismatchBefore =
                "!"
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__it.Before.Value!)), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__e))).IsEmpty";
            var mismatchAfter =
                "!"
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__e)), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__it.After.Value!))).IsEmpty";
            // Emitter-side branch on Before/After avoids unreachable runtime
            // string comparisons in generated code.
            if (side == "Before")
            {
                // Added keys must be absent before; all other changed keys must be
                // present with Before equal (fragment-aware).
                code.AppendLineAt(5, "if (__it.IsAdded) continue;");
                code.AppendLineAt(
                    5,
                    "bool __found = false; foreach (var __e in __cur"
                        + id
                        + ".Value!) if ("
                        + comparer
                        + ".Equals(__SparseKeyOf_ChangeSet_"
                        + id
                        + "(__e), __it.Key)) { __found = true; if ("
                        + mismatchBefore
                        + ") return false; break; }"
                );
                code.AppendLineAt(5, "if (!__found) return false;");
            }
            else
            {
                // Removed keys must be absent after; all other changed keys must be
                // present with After equal.
                code.AppendLineAt(5, "if (__it.IsRemoved) continue;");
                code.AppendLineAt(
                    5,
                    "bool __found = false; foreach (var __e in __cur"
                        + id
                        + ".Value!) if ("
                        + comparer
                        + ".Equals(__SparseKeyOf_ChangeSet_"
                        + id
                        + "(__e), __it.Key)) { __found = true; if ("
                        + mismatchAfter
                        + ") return false; break; }"
                );
                code.AppendLineAt(5, "if (!__found) return false;");
            }
        }
        else
        {
            // Scalar and fragment dictionary values share comparer-based endpoint
            // checks here (nested fragment deltas are validated by value equality
            // at the match-probe level; structural conflicts surface in rebase).
            EmitDictMatchProbes(code, id, side, facade);
        }
        code.AppendLineAt(4, "}");
        // Added keys must be absent on the Before side and present on the After side is
        // covered above per-item; missing keys otherwise match (disjoint paths).
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
    }

    private static void EmitDictMatchProbes(
        SharedIndentedBuilder code,
        int id,
        string side,
        string facade
    )
    {
        // Dictionary keys in generated models are reference types; the null-forgiving
        // operator satisfies interface-dictionary nullable analysis.
        if (side == "Before")
        {
            code.AppendLineAt(5, "if (__it.IsAdded) continue;");
            code.AppendLineAt(
                5,
                "if (!__cur"
                    + id
                    + ".Value!.TryGetValue(__it.Key!, out var __cv) || !"
                    + facade
                    + ".AreEqual((object?)__cv, (object?)__it.Before.Value)) return false;"
            );
        }
        else
        {
            code.AppendLineAt(5, "if (__it.IsRemoved) continue;");
            code.AppendLineAt(
                5,
                "if (!__cur"
                    + id
                    + ".Value!.TryGetValue(__it.Key!, out var __cv2) || !"
                    + facade
                    + ".AreEqual((object?)__cv2, (object?)__it.After.Value)) return false;"
            );
        }
    }

    private static void EmitDictGranularReturn(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string trans,
        string comparer,
        string keyType,
        string valueType,
        string editedType,
        bool hasPatch
    )
    {
        code.AppendLineAt(
            3,
            "var __stored = "
                + KeyedItems(member)
                + " ?? new global::System.Collections.Generic.List<"
                + trans
                + ".Item>();"
        );
        code.AppendLineAt(
            3,
            "var __added = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">("
                + comparer
                + "); var __removed = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">("
                + comparer
                + "); var __edited = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + editedType
                + ">("
                + comparer
                + ");"
        );
        if (hasPatch)
            code.AppendLineAt(
                3,
                "foreach (var __it in __stored) { if (__it.IsAdded) __added[__it.Key] = __it.After.Value!; else if (__it.IsRemoved) __removed[__it.Key] = __it.Before.Value!; else if (__it.IsEdited) __edited[__it.Key] = __it.Edit; }"
            );
        else
            code.AppendLineAt(
                3,
                "foreach (var __it in __stored) { if (__it.IsAdded) __added[__it.Key] = __it.After.Value!; else if (__it.IsRemoved) __removed[__it.Key] = __it.Before.Value!; else if (__it.IsEdited) __edited[__it.Key] = __it.After.Value!; }"
            );
        code.AppendLineAt(
            3,
            "return new "
                + trans
                + "(default, default, __added, __removed, __edited, __stored, false);"
        );
    }

    private static void EmitDictEmptyReturn(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string trans,
        string comparer
    )
    {
        var emptyEdited =
            (member.Collection.ValueType?.IsFragmentModel == true)
                ? ValueChangeSetOf(member)
                : ValueTypeOf(member);
        code.AppendLineAt(
            4,
            "return new "
                + trans
                + "(default, default, new global::System.Collections.Generic.Dictionary<"
                + KeyTypeOf(member)
                + ", "
                + ValueTypeOf(member)
                + ">("
                + comparer
                + "), new global::System.Collections.Generic.Dictionary<"
                + KeyTypeOf(member)
                + ", "
                + ValueTypeOf(member)
                + ">("
                + comparer
                + "), new global::System.Collections.Generic.Dictionary<"
                + KeyTypeOf(member)
                + ", "
                + emptyEdited
                + ">("
                + comparer
                + "), new global::System.Collections.Generic.List<"
                + trans
                + ".Item>(), true);"
        );
    }

    private static void AppendRebase(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment,
        string rebaseResult,
        string prefix,
        string rebase,
        string between,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        _ = between;
        _ = prefix;
        var conflict = dialect.ConflictType;
        var conflictKind = dialect.ConflictKindType;
        var conflictList = "global::System.Collections.Generic.List<" + dialect.ConflictType + ">";
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
        var __hasSparseRb = members.Any(static m => IsKeyed(m) || IsDict(m));
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
        code.AppendLineAt(4, "var __conf = new " + conflictList + "();");
        code.AppendLineAt(
            4,
            "__conf.Add(new "
                + conflict
                + "(new string[0], "
                + conflictKind
                + ".WholeContribution, __SparseState(__sparse_hasWhole ? __sparse_wholeBefore : default), __SparseState(__sparse_hasWhole ? __sparse_wholeAfter : default), __SparseState(current), \"The contribution conflicts with a concurrent change.\"));"
        );
        code.AppendLineAt(4, "return new " + rebaseResult + "(Between(current, current), __conf);");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "var __cur = current.Value!;");
        code.AppendLineAt(3, "var __conflicts = new " + conflictList + "();");
        // Declare rebased locals.
        ComputePublicNames(members, out _, out var __transNames);
        foreach (var member in members)
        {
            if (IsNested(member))
            {
                code.AppendLineAt(
                    3,
                    ChildChangeSet(member, dialect) + "? __r" + member.Id + " = null;"
                );
            }
            else if (IsKeyed(member) || IsDict(member))
            {
                var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
                var trans = __transNames[member.Id];
                var keyType = KeyTypeOf(member);
                code.AppendLineAt(3, "bool __rh" + member.Id + " = false;");
                code.AppendLineAt(3, "bool __rwhole" + member.Id + " = false;");
                code.AppendLineAt(3, opt + " __rwb" + member.Id + " = default;");
                code.AppendLineAt(3, opt + " __rwa" + member.Id + " = default;");
                code.AppendLineAt(
                    3,
                    "global::System.Collections.Generic.List<"
                        + trans
                        + ".Item>? __ritems"
                        + member.Id
                        + " = null;"
                );
                if (IsKeyed(member))
                {
                    code.AppendLineAt(
                        3,
                        "global::System.Collections.Generic.List<"
                            + keyType
                            + ">? __rbO"
                            + member.Id
                            + " = null;"
                    );
                    code.AppendLineAt(
                        3,
                        "global::System.Collections.Generic.List<"
                            + keyType
                            + ">? __raO"
                            + member.Id
                            + " = null;"
                    );
                }
            }
            else
            {
                var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
                code.AppendLineAt(3, opt + " __rb" + member.Id + " = default;");
                code.AppendLineAt(3, opt + " __ra" + member.Id + " = default;");
                code.AppendLineAt(3, "bool __rh" + member.Id + " = false;");
            }
        }
        if (__hasSparseRb)
            AppendPragmaDisableNullKey(code, 3);
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
            else if (IsKeyed(member))
            {
                AppendKeyedRebaseSparse(code, members, member, esc, lit, runtime, dialect);
            }
            else if (IsDict(member))
            {
                AppendDictRebaseSparse(code, members, member, esc, lit, runtime, dialect);
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
                    "        __conflicts.Add(new "
                        + conflict
                        + "(new string[] { "
                        + lit
                        + " }, "
                        + conflictKind
                        + ".CustomStrategy, __SparseMember(__base"
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
            else if (member.MergeMode is 2 or 3)
            {
                AppendMergeCollectionRebase(code, member, esc, lit, runtime, dialect);
            }
            else
            {
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
                    "        __conflicts.Add(new "
                        + conflict
                        + "(new string[] { "
                        + lit
                        + " }, "
                        + conflictKind
                        + ".Scalar, __SparseMember(__base"
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
            else if (IsKeyed(member))
                rargs.AddRange(
                    new[]
                    {
                        "__rh" + member.Id,
                        "__rwhole" + member.Id,
                        "__rwb" + member.Id,
                        "__rwa" + member.Id,
                        "__ritems" + member.Id,
                        "__rbO" + member.Id,
                        "__raO" + member.Id,
                    }
                );
            else if (IsDict(member))
                rargs.AddRange(
                    new[]
                    {
                        "__rh" + member.Id,
                        "__rwhole" + member.Id,
                        "__rwb" + member.Id,
                        "__rwa" + member.Id,
                        "__ritems" + member.Id,
                    }
                );
            else
                rargs.AddRange(
                    new[] { "__rb" + member.Id, "__ra" + member.Id, "__rh" + member.Id }
                );
        }
        code.AppendLineAt(3, "var __rebased = new ChangeSet(" + string.Join(", ", rargs) + ");");
        if (__hasSparseRb)
            AppendPragmaRestoreNullKey(code, 3);
        code.AppendLineAt(3, "return new " + rebaseResult + "(__rebased, __conflicts);");
        code.AppendLineAt(2, "}");
    }

    /// <summary>Emits sparse per-key rebase for a keyed member (issue #103).</summary>
    private static void AppendKeyedRebaseSparse(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseMemberModel member,
        string esc,
        string lit,
        string runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var id = member.Id;
        var keyType = KeyTypeOf(member);
        var elementType = ElementTypeOf(member);
        var elementCs = ElementChangeSetOf(member);
        var elementFrag = ElementFragmentOf(member);
        var optElement = runtime + "Optional<" + elementType + ">";
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var facade = dialect.RuntimeFacade;
        var conflict = dialect.ConflictType;
        var conflictKind = dialect.ConflictKindType;
        var trans = TransNameFor(members, member);
        code.AppendLineAt(4, "if (" + HasField(member) + ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(4, "var __curM" + id + " = __cur." + esc + ";");
        // Whole presence/null transitions: replay via member patch rebase is not sparse;
        // report member conflict unless already applied, else keep whole.
        code.AppendLineAt(4, "if (" + KeyedWholeFlag(member) + ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (Fragment.__SparseEqual_"
                + id
                + "("
                + KeyedWholeAfter(member)
                + ", __curM"
                + id
                + ")) { }"
        );
        code.AppendLineAt(
            5,
            "else if (Fragment.__SparseEqual_"
                + id
                + "("
                + KeyedWholeBefore(member)
                + ", __curM"
                + id
                + ")) { __rh"
                + id
                + " = true; __rwhole"
                + id
                + " = true; __rwb"
                + id
                + " = __curM"
                + id
                + "; __rwa"
                + id
                + " = "
                + KeyedWholeAfter(member)
                + "; }"
        );
        code.AppendLineAt(
            5,
            "else { __conflicts.Add(new "
                + conflict
                + "(new string[] { "
                + lit
                + " }, "
                + conflictKind
                + ".Nested, __SparseMember("
                + KeyedWholeBefore(member)
                + "), __SparseMember("
                + KeyedWholeAfter(member)
                + "), __SparseMember(__curM"
                + id
                + "), \"The member conflicts with a concurrent change.\")); }"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "else if (!__curM" + id + ".IsPresent || (object?)__curM" + id + ".Value is null)"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "__conflicts.Add(new "
                + conflict
                + "(new string[] { "
                + lit
                + " }, "
                + conflictKind
                + ".Nested, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Missing, __SparseMember(__curM"
                + id
                + "), \"The member conflicts with a concurrent change.\"));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        // Current map + order (transient, not retained).
        code.AppendLineAt(
            5,
            "var __cmap"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            5,
            "var __corder"
                + id
                + " = new global::System.Collections.Generic.List<"
                + keyType
                + ">();"
        );
        code.AppendLineAt(
            5,
            "foreach (var __e in __curM"
                + id
                + ".Value!) { var __ck = __SparseKeyOf_ChangeSet_"
                + id
                + "(__e); if (!__cmap"
                + id
                + ".TryAdd(__ck, __e)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection.\"); __corder"
                + id
                + ".Add(__ck); }"
        );
        code.AppendLineAt(
            5,
            "var __rlist"
                + id
                + " = new global::System.Collections.Generic.List<"
                + trans
                + ".Item>();"
        );
        code.AppendLineAt(5, "bool __any" + id + " = false;");
        code.AppendLineAt(
            5,
            "if ("
                + KeyedItems(member)
                + " is not null) foreach (var __it in "
                + KeyedItems(member)
                + ")"
        );
        code.AppendLineAt(5, "{");
        // Added: replay when absent; already-applied when equal; conflict otherwise.
        code.AppendLineAt(6, "if (__it.IsAdded)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            6,
            "if (!__cmap"
                + id
                + ".ContainsKey(__it.Key)) { __any"
                + id
                + " = true; "
                + optElement
                + " __na = "
                + optElement
                + ".Present(__it.After.Value!); "
                + runtime
                + "Optional<"
                + elementFrag
                + "?> __nea = "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__it.After.Value!)); var __nedit = "
                + elementCs
                + ".Between(default, __nea); __rlist"
                + id
                + ".Add(new "
                + trans
                + ".Item(__it.Key, default, __na, -1, __corder"
                + id
                + ".Count, true, false, false, false, __nedit, false)); __cmap"
                + id
                + "[__it.Key] = __it.After.Value!; __corder"
                + id
                + ".Add(__it.Key); }"
        );
        code.AppendLineAt(
            6,
            "else if (!"
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__cmap"
                + id
                + "[__it.Key])), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__it.After.Value!))).IsEmpty)"
        );
        code.AppendLineAt(
            6,
            "{ __conflicts.Add(new "
                + conflict
                + "(new string[] { "
                + lit
                + ", ((object?)__it.Key)?.ToString() ?? \"<null>\" }, "
                + conflictKind
                + ".Nested, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Present((object?)__it.After.Value), "
                + runtime
                + "Optional<object?>.Present((object?)__cmap"
                + id
                + "[__it.Key]), \"The keyed element conflicts with a concurrent change.\")); }"
        );
        code.AppendLineAt(6, "}");
        // Removed: already-applied when absent; replay when still base; conflict otherwise.
        code.AppendLineAt(6, "else if (__it.IsRemoved)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(6, "if (!__cmap" + id + ".ContainsKey(__it.Key)) { }");
        code.AppendLineAt(
            6,
            "else if (!"
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__cmap"
                + id
                + "[__it.Key])), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__it.Before.Value!))).IsEmpty)"
        );
        code.AppendLineAt(
            6,
            "{ __conflicts.Add(new "
                + conflict
                + "(new string[] { "
                + lit
                + ", ((object?)__it.Key)?.ToString() ?? \"<null>\" }, "
                + conflictKind
                + ".Nested, "
                + runtime
                + "Optional<object?>.Present((object?)__it.Before.Value), "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Present((object?)__cmap"
                + id
                + "[__it.Key]), \"The keyed element conflicts with a concurrent change.\")); }"
        );
        code.AppendLineAt(
            6,
            "else { __any"
                + id
                + " = true; int __bi = __corder"
                + id
                + ".IndexOf(__it.Key, 0); __rlist"
                + id
                + ".Add(new "
                + trans
                + ".Item(__it.Key, __it.Before, default, __bi, -1, false, true, false, false, __it.Edit, false)); __cmap"
                + id
                + ".Remove(__it.Key); }"
        );
        code.AppendLineAt(6, "}");
        // Edited: nested rebase distinguishes clean/already-applied/conflict.
        code.AppendLineAt(6, "else if (__it.IsEdited)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(6, "if (!__cmap" + id + ".TryGetValue(__it.Key, out var __cev))");
        code.AppendLineAt(
            6,
            "{ __conflicts.Add(new "
                + conflict
                + "(new string[] { "
                + lit
                + ", ((object?)__it.Key)?.ToString() ?? \"<null>\" }, "
                + conflictKind
                + ".Nested, "
                + runtime
                + "Optional<object?>.Present((object?)__it.Before.Value), "
                + runtime
                + "Optional<object?>.Present((object?)__it.After.Value), "
                + runtime
                + "Optional<object?>.Missing, \"The keyed element conflicts with a concurrent change.\")); }"
        );
        code.AppendLineAt(6, "else");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "var __nr = __it.Edit.RebaseOnto("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__cev)));"
        );
        code.AppendLineAt(
            7,
            "foreach (var __cc in __nr.Conflicts) __conflicts.Add(__cc.WithPathPrefix("
                + lit
                + "));"
        );
        code.AppendLineAt(7, "if (__nr.Conflicts.Count == 0 && !__nr.Patch.IsEmpty)");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(
            8,
            "var __applied = __nr.Patch.ToPatch().Apply("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__cev)));"
        );
        code.AppendLineAt(8, "if (__applied.IsPresent && __applied.Value is not null)");
        code.AppendLineAt(8, "{");
        code.AppendLineAt(8, "var __um = __applied.Value!.ToModel(); __any" + id + " = true;");
        code.AppendLineAt(
            8,
            runtime
                + "Optional<"
                + elementType
                + "> __nb = "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__cev); "
                + runtime
                + "Optional<"
                + elementType
                + "> __na2 = "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__um);"
        );
        code.AppendLineAt(8, "int __bi2 = __corder" + id + ".IndexOf(__it.Key, 0);");
        code.AppendLineAt(
            8,
            "__rlist"
                + id
                + ".Add(new "
                + trans
                + ".Item(__it.Key, __nb, __na2, __bi2, __bi2, false, false, true, __it.IsReordered, __nr.Patch, false)); __cmap"
                + id
                + "[__it.Key] = __um;"
        );
        code.AppendLineAt(8, "}");
        code.AppendLineAt(7, "}");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "}");
        // Reorder-only: keep when current order still matches base rank; else coincidentally ordered.
        code.AppendLineAt(6, "else if (__it.IsReordered)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            6,
            "if (__cmap"
                + id
                + ".ContainsKey(__it.Key)) { __any"
                + id
                + " = true; int __bi3 = __corder"
                + id
                + ".IndexOf(__it.Key, 0); __rlist"
                + id
                + ".Add(new "
                + trans
                + ".Item(__it.Key, __it.Before, __it.After, __bi3, __bi3, false, false, false, true, __it.Edit, false)); }"
        );
        code.AppendLineAt(6, "}");
        code.AppendLineAt(5, "}");
        // Order reconciliation: no local order change preserves current order
        // (concurrent adds kept); a local reorder is kept only when the base
        // order still matches current, else an order conflict is reported.
        code.AppendLineAt(
            5,
            "global::System.Collections.Generic.List<"
                + keyType
                + ">? __nbO"
                + id
                + " = null; global::System.Collections.Generic.List<"
                + keyType
                + ">? __naO"
                + id
                + " = null;"
        );
        code.AppendLineAt(
            5,
            "if ("
                + KeyedBeforeOrder(member)
                + " is not null && "
                + KeyedAfterOrder(member)
                + " is not null && "
                + facade
                + ".KeyOrderEquals<"
                + keyType
                + ">("
                + KeyedBeforeOrder(member)
                + ", "
                + KeyedAfterOrder(member)
                + ")) { __nbO"
                + id
                + " = new global::System.Collections.Generic.List<"
                + keyType
                + ">(__corder"
                + id
                + "); __naO"
                + id
                + " = new global::System.Collections.Generic.List<"
                + keyType
                + ">(__corder"
                + id
                + "); }"
        );
        code.AppendLineAt(
            5,
            "else if ("
                + KeyedBeforeOrder(member)
                + " is not null && "
                + KeyedAfterOrder(member)
                + " is not null)"
        );
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if ("
                + facade
                + ".KeyOrderEquals<"
                + keyType
                + ">("
                + KeyedBeforeOrder(member)
                + ", __corder"
                + id
                + ")) { __nbO"
                + id
                + " = new global::System.Collections.Generic.List<"
                + keyType
                + ">(__corder"
                + id
                + "); __naO"
                + id
                + " = new global::System.Collections.Generic.List<"
                + keyType
                + ">("
                + KeyedAfterOrder(member)
                + "); }"
        );
        code.AppendLineAt(
            6,
            "else if (!"
                + facade
                + ".KeyOrderEquals<"
                + keyType
                + ">("
                + KeyedAfterOrder(member)
                + ", __corder"
                + id
                + ")) { __conflicts.Add(new "
                + conflict
                + "(new string[] { "
                + lit
                + ", \"order\" }, "
                + conflictKind
                + ".Nested, "
                + runtime
                + "Optional<object?>.Present((object?)"
                + KeyedBeforeOrder(member)
                + "), "
                + runtime
                + "Optional<object?>.Present((object?)"
                + KeyedAfterOrder(member)
                + "), "
                + runtime
                + "Optional<object?>.Present((object?)__corder"
                + id
                + "), \"The collection order conflicts with a concurrent change.\")); }"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(
            5,
            "if (__any"
                + id
                + ") { __rh"
                + id
                + " = true; __ritems"
                + id
                + " = __rlist"
                + id
                + "; __rbO"
                + id
                + " = __nbO"
                + id
                + "; __raO"
                + id
                + " = __naO"
                + id
                + "; }"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "}");
    }

    /// <summary>Emits sparse per-key rebase for a dictionary member (issue #103).</summary>
    private static void AppendDictRebaseSparse(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseMemberModel member,
        string esc,
        string lit,
        string runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var id = member.Id;
        var valueType = ValueTypeOf(member);
        var optValue = runtime + "Optional<" + valueType + ">";
        var facade = dialect.RuntimeFacade;
        var conflict = dialect.ConflictType;
        var conflictKind = dialect.ConflictKindType;
        var hasPatch = member.Collection.ValueType?.IsFragmentModel == true;
        var valueFrag = hasPatch ? ValueFragmentOf(member) : null;
        var trans = TransNameFor(members, member);
        code.AppendLineAt(4, "if (" + HasField(member) + ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(4, "var __curM" + id + " = __cur." + esc + ";");
        code.AppendLineAt(4, "if (" + KeyedWholeFlag(member) + ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (Fragment.__SparseEqual_"
                + id
                + "("
                + KeyedWholeAfter(member)
                + ", __curM"
                + id
                + ")) { }"
        );
        code.AppendLineAt(
            5,
            "else if (Fragment.__SparseEqual_"
                + id
                + "("
                + KeyedWholeBefore(member)
                + ", __curM"
                + id
                + ")) { __rh"
                + id
                + " = true; __rwhole"
                + id
                + " = true; __rwb"
                + id
                + " = __curM"
                + id
                + "; __rwa"
                + id
                + " = "
                + KeyedWholeAfter(member)
                + "; }"
        );
        code.AppendLineAt(
            5,
            "else { __conflicts.Add(new "
                + conflict
                + "(new string[] { "
                + lit
                + " }, "
                + conflictKind
                + ".Nested, __SparseMember("
                + KeyedWholeBefore(member)
                + "), __SparseMember("
                + KeyedWholeAfter(member)
                + "), __SparseMember(__curM"
                + id
                + "), \"The member conflicts with a concurrent change.\")); }"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "else if (!__curM" + id + ".IsPresent || (object?)__curM" + id + ".Value is null)"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "__conflicts.Add(new "
                + conflict
                + "(new string[] { "
                + lit
                + " }, "
                + conflictKind
                + ".Nested, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Missing, __SparseMember(__curM"
                + id
                + "), \"The member conflicts with a concurrent change.\"));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "var __rlist"
                + id
                + " = new global::System.Collections.Generic.List<"
                + trans
                + ".Item>();"
        );
        code.AppendLineAt(5, "bool __any" + id + " = false;");
        code.AppendLineAt(
            5,
            "if ("
                + KeyedItems(member)
                + " is not null) foreach (var __it in "
                + KeyedItems(member)
                + ")"
        );
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "if (__it.IsAdded)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(6, "if (!__curM" + id + ".Value!.ContainsKey(__it.Key!))");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(7, "__any" + id + " = true;");
        if (hasPatch)
            code.AppendLineAt(
                7,
                "__rlist"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__it.Key!, default, __it.After, true, false, false, __it.Edit, false));"
            );
        else
            code.AppendLineAt(
                7,
                "__rlist"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__it.Key!, default, __it.After, true, false, false, false));"
            );
        code.AppendLineAt(6, "}");
        code.AppendLineAt(
            6,
            "else if (!"
                + facade
                + ".AreEqual((object?)__curM"
                + id
                + ".Value![__it.Key!], (object?)__it.After.Value))"
        );
        code.AppendLineAt(
            6,
            "{ __conflicts.Add(new "
                + conflict
                + "(new string[] { "
                + lit
                + ", ((object?)__it.Key!)?.ToString() ?? \"<null>\" }, "
                + conflictKind
                + ".Nested, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Present((object?)__it.After.Value), "
                + runtime
                + "Optional<object?>.Present((object?)__curM"
                + id
                + ".Value![__it.Key!]), \"The dictionary entry conflicts with a concurrent change.\")); }"
        );
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "else if (__it.IsRemoved)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(6, "if (!__curM" + id + ".Value!.ContainsKey(__it.Key!)) { }");
        code.AppendLineAt(
            6,
            "else if (!"
                + facade
                + ".AreEqual((object?)__curM"
                + id
                + ".Value![__it.Key!], (object?)__it.Before.Value))"
        );
        code.AppendLineAt(
            6,
            "{ __conflicts.Add(new "
                + conflict
                + "(new string[] { "
                + lit
                + ", ((object?)__it.Key!)?.ToString() ?? \"<null>\" }, "
                + conflictKind
                + ".Nested, "
                + runtime
                + "Optional<object?>.Present((object?)__it.Before.Value), "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Present((object?)__curM"
                + id
                + ".Value![__it.Key!]), \"The dictionary entry conflicts with a concurrent change.\")); }"
        );
        code.AppendLineAt(6, "else");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(7, "__any" + id + " = true;");
        if (hasPatch)
            code.AppendLineAt(
                7,
                "__rlist"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__it.Key!, __it.Before, default, false, true, false, __it.Edit, false));"
            );
        else
            code.AppendLineAt(
                7,
                "__rlist"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__it.Key!, __it.Before, default, false, true, false, false));"
            );
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "else");
        code.AppendLineAt(6, "{");
        if (hasPatch)
        {
            code.AppendLineAt(
                6,
                "if (!__curM" + id + ".Value!.TryGetValue(__it.Key!, out var __cev))"
            );
            code.AppendLineAt(
                6,
                "{ __conflicts.Add(new "
                    + conflict
                    + "(new string[] { "
                    + lit
                    + ", ((object?)__it.Key!)?.ToString() ?? \"<null>\" }, "
                    + conflictKind
                    + ".Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)__it.Before.Value), "
                    + runtime
                    + "Optional<object?>.Present((object?)__it.After.Value), "
                    + runtime
                    + "Optional<object?>.Missing, \"The dictionary entry conflicts with a concurrent change.\")); }"
            );
            code.AppendLineAt(6, "else");
            code.AppendLineAt(6, "{");
            code.AppendLineAt(
                7,
                "var __nr = __it.Edit.RebaseOnto("
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__cev)));"
            );
            code.AppendLineAt(
                7,
                "foreach (var __cc in __nr.Conflicts) __conflicts.Add(__cc.WithPathPrefix("
                    + lit
                    + "));"
            );
            code.AppendLineAt(7, "if (__nr.Conflicts.Count == 0 && !__nr.Patch.IsEmpty)");
            code.AppendLineAt(7, "{");
            code.AppendLineAt(
                8,
                "var __applied = __nr.Patch.ToPatch().Apply("
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__cev)));"
            );
            code.AppendLineAt(8, "if (__applied.IsPresent && __applied.Value is not null)");
            code.AppendLineAt(8, "{");
            code.AppendLineAt(8, "__any" + id + " = true;");
            code.AppendLineAt(
                8,
                optValue
                    + " __nb = "
                    + optValue
                    + ".Present(__cev); "
                    + optValue
                    + " __na2 = "
                    + optValue
                    + ".Present(__applied.Value!.ToModel());"
            );
            code.AppendLineAt(
                8,
                "__rlist"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__it.Key!, __nb, __na2, false, false, true, __nr.Patch, false));"
            );
            code.AppendLineAt(8, "}");
            code.AppendLineAt(7, "}");
            code.AppendLineAt(6, "}");
        }
        else
        {
            code.AppendLineAt(
                6,
                "if (!__curM" + id + ".Value!.TryGetValue(__it.Key!, out var __cev2))"
            );
            code.AppendLineAt(
                6,
                "{ __conflicts.Add(new "
                    + conflict
                    + "(new string[] { "
                    + lit
                    + ", ((object?)__it.Key!)?.ToString() ?? \"<null>\" }, "
                    + conflictKind
                    + ".Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)__it.Before.Value), "
                    + runtime
                    + "Optional<object?>.Present((object?)__it.After.Value), "
                    + runtime
                    + "Optional<object?>.Missing, \"The dictionary entry conflicts with a concurrent change.\")); }"
            );
            code.AppendLineAt(
                6,
                "else if (" + facade + ".AreEqual((object?)__cev2, (object?)__it.After.Value)) { }"
            );
            code.AppendLineAt(
                6,
                "else if (!" + facade + ".AreEqual((object?)__cev2, (object?)__it.Before.Value))"
            );
            code.AppendLineAt(
                6,
                "{ __conflicts.Add(new "
                    + conflict
                    + "(new string[] { "
                    + lit
                    + ", ((object?)__it.Key!)?.ToString() ?? \"<null>\" }, "
                    + conflictKind
                    + ".Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)__it.Before.Value), "
                    + runtime
                    + "Optional<object?>.Present((object?)__it.After.Value), "
                    + runtime
                    + "Optional<object?>.Present((object?)__cev2), \"The dictionary entry conflicts with a concurrent change.\")); }"
            );
            code.AppendLineAt(6, "else");
            code.AppendLineAt(6, "{");
            code.AppendLineAt(7, "__any" + id + " = true;");
            code.AppendLineAt(
                7,
                "__rlist"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__it.Key!, __it.Before, __it.After, false, false, true, false));"
            );
            code.AppendLineAt(6, "}");
        }
        code.AppendLineAt(6, "}");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(
            5,
            "if (__any"
                + id
                + ") { __rh"
                + id
                + " = true; __ritems"
                + id
                + " = __rlist"
                + id
                + "; }"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "}");
    }

    /// <summary>
    /// Emits merge-aware rebase for Append/SetUnion scalar-collection members (issue #97).
    /// </summary>
    /// <remarks>
    /// Unlike plain scalar equality, Append members replay the locally appended suffix onto
    /// the current prefix and SetUnion members replay locally added elements beside the
    /// current set; both consume only the retained per-member before/desired plus the
    /// supplied current member, mirroring the generated Patch rebase dispatch (typed
    /// set/sequence fast paths with a boxed semantic fallback). A rebased value equal to
    /// current normalizes to no-op; an unmergeable concurrent change reports a structured
    /// member conflict while leaving other paths untouched.
    /// </remarks>
    private static void AppendMergeCollectionRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string esc,
        string lit,
        string runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var id = member.Id;
        var valueType = FragmentValueType(member);
        var elementType = member.Collection.ElementType.Name;
        var opt = runtime + "Optional<" + valueType + ">";
        var facade = dialect.RuntimeFacade;
        var comparer = dialect.RuntimeFacade;
        var conflict = dialect.ConflictType;
        var kind =
            member.MergeMode == 2
                ? dialect.ConflictKindType + ".CollectionAppend"
                : dialect.ConflictKindType + ".CollectionSetUnion";
        var isSet =
            member.MergeMode == 3 && member.Collection.CloneKind == SparseCloneCollectionKind.Set;
        var isTypedSequence =
            !isSet
            && member.Collection.CloneKind
                is SparseCloneCollectionKind.Array
                    or SparseCloneCollectionKind.List
            && member.Collection.ElementType.UsesDefaultScalarEquality;

        code.AppendLineAt(4, "if (" + HasField(member) + ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(4, "    var __base" + id + " = " + BeforeField(member) + ";");
        code.AppendLineAt(4, "    var __des" + id + " = " + AfterField(member) + ";");
        code.AppendLineAt(4, "    var __curM" + id + " = __cur." + esc + ";");
        code.AppendLineAt(
            4,
            "    if (Fragment.__SparseEqual_" + id + "(__base" + id + ", __curM" + id + "))"
        );
        code.AppendLineAt(4, "    {");
        code.AppendLineAt(5, "        __rh" + id + " = true;");
        code.AppendLineAt(5, "        __rb" + id + " = __curM" + id + ";");
        code.AppendLineAt(5, "        __ra" + id + " = __des" + id + ";");
        code.AppendLineAt(4, "    }");
        code.AppendLineAt(
            4,
            "    else if (__base"
                + id
                + ".IsPresent && __des"
                + id
                + ".IsPresent && __curM"
                + id
                + ".IsPresent && (object?)__base"
                + id
                + ".Value is not null && (object?)__des"
                + id
                + ".Value is not null && (object?)__curM"
                + id
                + ".Value is not null)"
        );
        code.AppendLineAt(4, "    {");
        code.AppendLineAt(5, "        " + valueType + " __rebuilt" + id + " = default!;");
        code.AppendLineAt(5, "        string? __reason" + id + ";");
        code.AppendLineAt(5, "        bool __ok" + id + ";");
        if (isSet)
        {
            code.AppendLineAt(
                5,
                "        __ok"
                    + id
                    + " = "
                    + facade
                    + ".TryRebaseSetUnion<"
                    + elementType
                    + ">(__base"
                    + id
                    + ".Value!, __des"
                    + id
                    + ".Value!, __curM"
                    + id
                    + ".Value!, out var __rv"
                    + id
                    + ", out __reason"
                    + id
                    + ");"
            );
            code.AppendLineAt(
                5,
                "        if (__ok" + id + ") __rebuilt" + id + " = __rv" + id + ";"
            );
        }
        else if (isTypedSequence)
        {
            var typedMethod =
                member.MergeMode == 2 ? "TryRebaseSequenceAppend" : "TryRebaseSequenceSetUnion";
            if (member.Collection.CloneKind == SparseCloneCollectionKind.Array)
                typedMethod += "Array";
            var boxedMethod = member.MergeMode == 2 ? "TryRebaseAppend" : "TryRebaseSetUnion";
            var readOnly = "global::System.Collections.Generic.IReadOnlyList<" + elementType + ">";
            var list = "global::System.Collections.Generic.List<" + elementType + ">";
            string NativeInput(string state, string variable) =>
                "(object?)"
                + state
                + ".Value is "
                + readOnly
                + " "
                + variable
                + " && ("
                + variable
                + " is "
                + elementType
                + "[] || "
                + variable
                + ".GetType() == typeof("
                + list
                + "))";
            code.AppendLineAt(
                5,
                "        if ("
                    + NativeInput("__base" + id, "__bv" + id)
                    + " && "
                    + NativeInput("__des" + id, "__dv" + id)
                    + " && "
                    + NativeInput("__curM" + id, "__cv" + id)
                    + ")"
            );
            code.AppendLineAt(5, "        {");
            code.AppendLineAt(
                6,
                "            __ok"
                    + id
                    + " = "
                    + facade
                    + "."
                    + typedMethod
                    + "<"
                    + elementType
                    + ">(__bv"
                    + id
                    + ", __dv"
                    + id
                    + ", __cv"
                    + id
                    + ", null, out var __tv"
                    + id
                    + ", out __reason"
                    + id
                    + ");"
            );
            code.AppendLineAt(
                6,
                "            if (__ok" + id + ") __rebuilt" + id + " = __tv" + id + ";"
            );
            code.AppendLineAt(5, "        }");
            code.AppendLineAt(5, "        else");
            code.AppendLineAt(5, "        {");
            code.AppendLineAt(
                6,
                "            var __bb"
                    + id
                    + " = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)__base"
                    + id
                    + ".Value));"
            );
            code.AppendLineAt(
                6,
                "            var __cb"
                    + id
                    + " = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)__curM"
                    + id
                    + ".Value));"
            );
            code.AppendLineAt(
                6,
                "            var __db"
                    + id
                    + " = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)__des"
                    + id
                    + ".Value));"
            );
            code.AppendLineAt(
                6,
                "            __ok"
                    + id
                    + " = "
                    + facade
                    + "."
                    + boxedMethod
                    + "(__bb"
                    + id
                    + ", __db"
                    + id
                    + ", __cb"
                    + id
                    + ", (object? __l, object? __r) => "
                    + comparer
                    + ".AreEqual(__l, __r), out var __bx"
                    + id
                    + ", out __reason"
                    + id
                    + ");"
            );
            var boxedResult = SparseFragmentExpressions.MaterializeCollection(
                member,
                "global::System.Linq.Enumerable.Cast<" + elementType + ">(__bx" + id + ")"
            );
            code.AppendLineAt(
                6,
                "            if (__ok" + id + ") __rebuilt" + id + " = " + boxedResult + ";"
            );
            code.AppendLineAt(5, "        }");
        }
        else
        {
            var boxedMethod = member.MergeMode == 2 ? "TryRebaseAppend" : "TryRebaseSetUnion";
            code.AppendLineAt(
                5,
                "        var __bb"
                    + id
                    + " = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)__base"
                    + id
                    + ".Value));"
            );
            code.AppendLineAt(
                5,
                "        var __cb"
                    + id
                    + " = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)__curM"
                    + id
                    + ".Value));"
            );
            code.AppendLineAt(
                5,
                "        var __db"
                    + id
                    + " = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)__des"
                    + id
                    + ".Value));"
            );
            code.AppendLineAt(
                5,
                "        __ok"
                    + id
                    + " = "
                    + facade
                    + "."
                    + boxedMethod
                    + "(__bb"
                    + id
                    + ", __db"
                    + id
                    + ", __cb"
                    + id
                    + ", (object? __l, object? __r) => "
                    + comparer
                    + ".AreEqual(__l, __r), out var __bx"
                    + id
                    + ", out __reason"
                    + id
                    + ");"
            );
            var boxedResult = SparseFragmentExpressions.MaterializeCollection(
                member,
                "global::System.Linq.Enumerable.Cast<" + elementType + ">(__bx" + id + ")"
            );
            code.AppendLineAt(
                5,
                "        if (__ok" + id + ") __rebuilt" + id + " = " + boxedResult + ";"
            );
        }
        code.AppendLineAt(5, "        if (__ok" + id + ")");
        code.AppendLineAt(5, "        {");
        code.AppendLineAt(
            6,
            "            " + opt + " __rebOpt" + id + " = " + opt + ".Present(__rebuilt" + id + ");"
        );
        code.AppendLineAt(
            6,
            "            if (!Fragment.__SparseEqual_"
                + id
                + "(__curM"
                + id
                + ", __rebOpt"
                + id
                + "))"
        );
        code.AppendLineAt(6, "            {");
        code.AppendLineAt(7, "                __rh" + id + " = true;");
        code.AppendLineAt(7, "                __rb" + id + " = __curM" + id + ";");
        code.AppendLineAt(7, "                __ra" + id + " = __rebOpt" + id + ";");
        code.AppendLineAt(6, "            }");
        code.AppendLineAt(5, "        }");
        code.AppendLineAt(5, "        else");
        code.AppendLineAt(5, "        {");
        code.AppendLineAt(
            6,
            "            __conflicts.Add(new "
                + conflict
                + "(new string[] { "
                + lit
                + " }, "
                + kind
                + ", __SparseMember(__base"
                + id
                + "), __SparseMember(__des"
                + id
                + "), __SparseMember(__curM"
                + id
                + "), __reason"
                + id
                + " ?? \"The member conflicts with a concurrent change.\"));"
        );
        code.AppendLineAt(5, "        }");
        code.AppendLineAt(4, "    }");
        code.AppendLineAt(
            4,
            "    else if (!Fragment.__SparseEqual_" + id + "(__des" + id + ", __curM" + id + "))"
        );
        code.AppendLineAt(4, "    {");
        code.AppendLineAt(
            5,
            "        __conflicts.Add(new "
                + conflict
                + "(new string[] { "
                + lit
                + " }, "
                + kind
                + ", __SparseMember(__base"
                + id
                + "), __SparseMember(__des"
                + id
                + "), __SparseMember(__curM"
                + id
                + "), \"The member conflicts with a concurrent change.\"));"
        );
        code.AppendLineAt(4, "    }");
        code.AppendLineAt(4, "}");
    }

    private static void AppendTypedSurface(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        if (members.IsDefaultOrEmpty)
            return;
        var runtime = dialect.RuntimeNamespace;
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
        // Sparse before/after helpers read canonical sparse storage (issue #96, #103).
        // Whole-root transitions project member states from the retained root
        // fragments; memberwise transitions expose only retained changed paths.
        // Keyed/dict granular transitions retain no full member snapshots: the
        // member Before/After project whole endpoints only for whole presence
        // transitions, and Missing otherwise.
        foreach (var member in members)
        {
            if (IsNested(member))
                continue;
            var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
            var esc = SparseNaming.EscapeIdentifier(member.Property.Name);
            if (IsKeyed(member) || IsDict(member))
            {
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
                        + " && "
                        + KeyedWholeFlag(member)
                        + " ? "
                        + KeyedWholeBefore(member)
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
                        + " && "
                        + KeyedWholeFlag(member)
                        + " ? "
                        + KeyedWholeAfter(member)
                        + " : default);"
                );
            }
            else
            {
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
        }
        foreach (var member in members)
        {
            var prop = SparseNaming.EscapeIdentifier(propNames[member.Id]);
            if (IsScalar(member))
                AppendScalarTransition(code, member, prop, transNames[member.Id], runtime);
            else if (IsNested(member))
                AppendNestedProperty(code, member, prop, dialect);
            else if (IsKeyed(member))
                AppendKeyedTransition(code, member, prop, transNames[member.Id], runtime, dialect);
            else if (IsDict(member))
                AppendDictTransition(code, member, prop, transNames[member.Id], runtime, dialect);
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
        string prop,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var childCs = ChildChangeSet(member, dialect);
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
        string runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
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
        var facade = dialect.RuntimeFacade;
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
        // Sparse projection (issue #103): derive typed transition from canonical
        // sparse storage without full member snapshots.
        code.AppendLineAt(2, "private " + trans + " __SparseProject_" + member.Id + "()");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (__sparse_hasWhole) return __SparseBuild_"
                + member.Id
                + "(__SparseBefore_"
                + member.Id
                + "(), __SparseAfter_"
                + member.Id
                + "());"
        );
        code.AppendLineAt(3, "if (!" + HasField(member) + ")");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "return new "
                + trans
                + "(default, default, new global::System.Collections.Generic.List<"
                + elementType
                + ">(), new global::System.Collections.Generic.List<"
                + elementType
                + ">(), new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementCs
                + ">("
                + comparer
                + "), new global::System.Collections.Generic.List<"
                + keyType
                + ">(), new global::System.Collections.Generic.List<"
                + keyType
                + ">(), false, new global::System.Collections.Generic.List<"
                + trans
                + ".Item>(), true);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "if ("
                + KeyedWholeFlag(member)
                + ") return __SparseBuild_"
                + member.Id
                + "("
                + KeyedWholeBefore(member)
                + ", "
                + KeyedWholeAfter(member)
                + ");"
        );
        code.AppendLineAt(
            3,
            "var __stored = "
                + KeyedItems(member)
                + " ?? new global::System.Collections.Generic.List<"
                + trans
                + ".Item>();"
        );
        code.AppendLineAt(
            3,
            "var __added = new global::System.Collections.Generic.List<"
                + elementType
                + ">(); var __removed = new global::System.Collections.Generic.List<"
                + elementType
                + ">(); var __edited = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementCs
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            3,
            "foreach (var __it in __stored) { if (__it.IsAdded) __added.Add(__it.After.Value!); else if (__it.IsRemoved) __removed.Add(__it.Before.Value!); else if (__it.IsEdited) __edited[__it.Key] = __it.Edit; }"
        );
        code.AppendLineAt(
            3,
            "var __bO = "
                + KeyedBeforeOrder(member)
                + " ?? new global::System.Collections.Generic.List<"
                + keyType
                + ">(); var __aO = "
                + KeyedAfterOrder(member)
                + " ?? new global::System.Collections.Generic.List<"
                + keyType
                + ">();"
        );
        code.AppendLineAt(
            3,
            "bool __oc = "
                + KeyedBeforeOrder(member)
                + " is not null && "
                + KeyedAfterOrder(member)
                + " is not null && !"
                + facade
                + ".KeyOrderEquals<"
                + keyType
                + ">(__bO, __aO);"
        );
        code.AppendLineAt(
            3,
            "return new "
                + trans
                + "(default, default, __added, __removed, __edited, __bO, __aO, __oc, __stored, false);"
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
            "public " + trans + " " + prop + " => __SparseProject_" + member.Id + "();"
        );
    }

    private static void AppendDictTransition(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string prop,
        string trans,
        string runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var keyType = KeyTypeOf(member);
        var valueType = ValueTypeOf(member);
        var dictType = member.Property.Type.Name;
        var optDict = runtime + "Optional<" + dictType + ">";
        var optValue = runtime + "Optional<" + valueType + ">";
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var facade = dialect.RuntimeFacade;
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
        // Sparse projection (issue #103) from canonical storage.
        code.AppendLineAt(2, "private " + trans + " __SparseProject_" + member.Id + "()");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (__sparse_hasWhole) return __SparseBuild_"
                + member.Id
                + "(__SparseBefore_"
                + member.Id
                + "(), __SparseAfter_"
                + member.Id
                + "());"
        );
        code.AppendLineAt(3, "if (!" + HasField(member) + ")");
        code.AppendLineAt(3, "{");
        EmitDictEmptyReturn(code, member, trans, comparer);
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "if ("
                + KeyedWholeFlag(member)
                + ") return __SparseBuild_"
                + member.Id
                + "("
                + KeyedWholeBefore(member)
                + ", "
                + KeyedWholeAfter(member)
                + ");"
        );
        // Granular: derive Added/Removed/Edited maps from stored items (no full snapshots).
        EmitDictGranularReturn(
            code,
            member,
            trans,
            comparer,
            keyType,
            valueType,
            editedType,
            hasPatch
        );
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
            "public " + trans + " " + prop + " => __SparseProject_" + member.Id + "();"
        );
    }

    /// <summary>
    /// Emits Between-time sparse diff for a keyed sequence member (issue #103).
    /// </summary>
    /// <remarks>
    /// Declares locals __h/&#95;_whole/&#95;_wb/&#95;_wa/&#95;_items/&#95;_bO/&#95;_aO.
    /// Whole presence/null transitions retain whole endpoints; granular
    /// Value-&gt;Value transitions retain only per-changed-key items (added-&gt;after,
    /// removed-&gt;before, edited nested ChangeSet + endpoints, reorder-only value)
    /// plus full key orders only when the order actually changed. Unchanged
    /// element values are never retained.
    /// </remarks>
    private static void AppendKeyedBetweenSparse(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string escName,
        string runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var id = member.Id;
        var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
        var keyType = KeyTypeOf(member);
        var elementType = ElementTypeOf(member);
        var elementCs = ElementChangeSetOf(member);
        var elementFrag = ElementFragmentOf(member);
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var facade = dialect.RuntimeFacade;
        ComputePublicNames(
            System.Collections.Immutable.ImmutableArray.Create(member),
            out _,
            out var transNames
        );
        var trans = transNames[member.Id];
        // Locals.
        code.AppendLineAt(3, "bool __h" + id + " = false;");
        code.AppendLineAt(3, "bool __whole" + id + " = false;");
        code.AppendLineAt(3, opt + " __wb" + id + " = default;");
        code.AppendLineAt(3, opt + " __wa" + id + " = default;");
        code.AppendLineAt(
            3,
            "global::System.Collections.Generic.List<" + trans + ".Item>? __items" + id + " = null;"
        );
        code.AppendLineAt(
            3,
            "global::System.Collections.Generic.List<" + keyType + ">? __bO" + id + " = null;"
        );
        code.AppendLineAt(
            3,
            "global::System.Collections.Generic.List<" + keyType + ">? __aO" + id + " = null;"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var __before" + id + " = bf." + escName + ";");
        code.AppendLineAt(4, "var __after" + id + " = af." + escName + ";");
        code.AppendLineAt(
            4,
            "bool __beforeHas"
                + id
                + " = __before"
                + id
                + ".IsPresent && (object?)__before"
                + id
                + ".Value is not null;"
        );
        code.AppendLineAt(
            4,
            "bool __afterHas"
                + id
                + " = __after"
                + id
                + ".IsPresent && (object?)__after"
                + id
                + ".Value is not null;"
        );
        // Whole when presence differs or null involved (exact Missing/null/value).
        code.AppendLineAt(
            4,
            "if (__before"
                + id
                + ".IsPresent != __after"
                + id
                + ".IsPresent || !__beforeHas"
                + id
                + " || !__afterHas"
                + id
                + ")"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (!Fragment.__SparseEqual_" + id + "(__before" + id + ", __after" + id + "))"
        );
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "__h" + id + " = true; __whole" + id + " = true;");
        code.AppendLineAt(
            6,
            "__wb" + id + " = __before" + id + "; __wa" + id + " = __after" + id + ";"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        // Granular Value -> Value: build key maps and orders (single enumeration each).
        code.AppendLineAt(
            5,
            "var __beforeMap"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            5,
            "var __beforeOrder"
                + id
                + " = new global::System.Collections.Generic.List<"
                + keyType
                + ">();"
        );
        code.AppendLineAt(5, "foreach (var __item in __before" + id + ".Value!)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "var __k = __SparseKeyOf_ChangeSet_" + id + "(__item);");
        code.AppendLineAt(
            6,
            "if (!__beforeMap"
                + id
                + ".TryAdd(__k, __item)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection.\");"
        );
        code.AppendLineAt(6, "__beforeOrder" + id + ".Add(__k);");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(
            5,
            "var __afterMap"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            5,
            "var __afterOrder"
                + id
                + " = new global::System.Collections.Generic.List<"
                + keyType
                + ">();"
        );
        code.AppendLineAt(5, "foreach (var __item in __after" + id + ".Value!)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "var __k = __SparseKeyOf_ChangeSet_" + id + "(__item);");
        code.AppendLineAt(
            6,
            "if (!__afterMap"
                + id
                + ".TryAdd(__k, __item)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection.\");"
        );
        code.AppendLineAt(6, "__afterOrder" + id + ".Add(__k);");
        code.AppendLineAt(5, "}");
        // Edited (nested sparse ChangeSets for surviving keys).
        code.AppendLineAt(
            5,
            "var __edited"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementCs
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(5, "foreach (var __k in __beforeMap" + id + ".Keys)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (!__afterMap" + id + ".TryGetValue(__k, out var __afterItem)) continue;"
        );
        code.AppendLineAt(6, "var __beforeItem = __beforeMap" + id + "[__k];");
        code.AppendLineAt(
            6,
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
        code.AppendLineAt(6, "if (!__nested.IsEmpty) __edited" + id + "[__k] = __nested;");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(
            5,
            "bool __orderChanged"
                + id
                + " = !"
                + facade
                + ".KeyOrderEquals<"
                + keyType
                + ">(__beforeOrder"
                + id
                + ", __afterOrder"
                + id
                + ");"
        );
        // Empty when no added/removed/edited and order unchanged.
        code.AppendLineAt(
            5,
            "bool __hasAdded"
                + id
                + " = false; foreach (var __k in __afterOrder"
                + id
                + ") if (!__beforeMap"
                + id
                + ".ContainsKey(__k)) { __hasAdded"
                + id
                + " = true; break; }"
        );
        code.AppendLineAt(
            5,
            "bool __hasRemoved"
                + id
                + " = false; foreach (var __k in __beforeOrder"
                + id
                + ") if (!__afterMap"
                + id
                + ".ContainsKey(__k)) { __hasRemoved"
                + id
                + " = true; break; }"
        );
        code.AppendLineAt(
            5,
            "if (!__hasAdded"
                + id
                + " && !__hasRemoved"
                + id
                + " && __edited"
                + id
                + ".Count == 0 && !__orderChanged"
                + id
                + ") { }"
        );
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "__h" + id + " = true;");
        // Surviving-rank reorder set (relative order, not absolute shifts).
        code.AppendLineAt(
            6,
            "var __beforeRank"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", int>("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            6,
            "{ var __r = 0; foreach (var __k in __beforeOrder"
                + id
                + ") if (__afterMap"
                + id
                + ".ContainsKey(__k)) __beforeRank"
                + id
                + "[__k] = __r++; }"
        );
        code.AppendLineAt(
            6,
            "var __afterRank"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", int>("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            6,
            "{ var __r = 0; foreach (var __k in __afterOrder"
                + id
                + ") if (__beforeMap"
                + id
                + ".ContainsKey(__k)) __afterRank"
                + id
                + "[__k] = __r++; }"
        );
        code.AppendLineAt(
            6,
            "var __reordered"
                + id
                + " = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            6,
            "foreach (var __kv in __beforeRank"
                + id
                + ") if (__afterRank"
                + id
                + ".TryGetValue(__kv.Key, out var __ar) && __ar != __kv.Value) __reordered"
                + id
                + ".Add(__kv.Key);"
        );
        code.AppendLineAt(
            6,
            "var __beforeIndex"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", int>("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            6,
            "for (var __i = 0; __i < __beforeOrder"
                + id
                + ".Count; __i++) __beforeIndex"
                + id
                + "[__beforeOrder"
                + id
                + "[__i]] = __i;"
        );
        code.AppendLineAt(
            6,
            "var __afterIndex"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", int>("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            6,
            "for (var __i = 0; __i < __afterOrder"
                + id
                + ".Count; __i++) __afterIndex"
                + id
                + "[__afterOrder"
                + id
                + "[__i]] = __i;"
        );
        code.AppendLineAt(
            6,
            "var __list"
                + id
                + " = new global::System.Collections.Generic.List<"
                + trans
                + ".Item>();"
        );
        // After-order changed keys (added / edited / reorder-only).
        code.AppendLineAt(6, "foreach (var __k in __afterOrder" + id + ")");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "bool __inBefore = __beforeMap" + id + ".TryGetValue(__k, out var __b);"
        );
        code.AppendLineAt(7, "var __a = __afterMap" + id + "[__k];");
        code.AppendLineAt(
            7,
            "bool __isEdited = __edited" + id + ".TryGetValue(__k, out var __edit);"
        );
        code.AppendLineAt(7, "bool __isReordered = __reordered" + id + ".Contains(__k);");
        code.AppendLineAt(7, "if (!__inBefore || __isEdited || __isReordered)");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(
            8,
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
            8,
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
            8,
            "var __fullEdit = __isEdited ? __edit! : " + elementCs + ".Between(__eb, __ea);"
        );
        code.AppendLineAt(
            8,
            runtime
                + "Optional<"
                + elementType
                + "> __ib = __inBefore ? "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__b!) : default;"
        );
        code.AppendLineAt(
            8,
            "int __bi = __beforeIndex" + id + ".TryGetValue(__k, out var __bv) ? __bv : -1;"
        );
        code.AppendLineAt(
            8,
            "int __ai = __afterIndex" + id + ".TryGetValue(__k, out var __av) ? __av : -1;"
        );
        code.AppendLineAt(
            8,
            "__list"
                + id
                + ".Add(new "
                + trans
                + ".Item(__k, __ib, "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__a), __bi, __ai, !__inBefore, false, __isEdited, __isReordered, __fullEdit, false));"
        );
        code.AppendLineAt(7, "}");
        code.AppendLineAt(6, "}");
        // Removed keys in before-order.
        code.AppendLineAt(6, "foreach (var __k in __beforeOrder" + id + ")");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(7, "if (__afterMap" + id + ".ContainsKey(__k)) continue;");
        code.AppendLineAt(7, "var __b = __beforeMap" + id + "[__k];");
        code.AppendLineAt(
            7,
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
        code.AppendLineAt(7, "var __fullEdit = " + elementCs + ".Between(__eb, default);");
        code.AppendLineAt(
            7,
            "int __bi = __beforeIndex" + id + ".TryGetValue(__k, out var __bv) ? __bv : -1;"
        );
        code.AppendLineAt(
            7,
            "__list"
                + id
                + ".Add(new "
                + trans
                + ".Item(__k, "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__b), default, __bi, -1, false, true, false, false, __fullEdit, false));"
        );
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "__items" + id + " = __list" + id + ";");
        // Always retain full key orders (keys only, never element payloads) so typed
        // BeforeOrder/AfterOrder preserve #94 absolute semantics; OrderChanged (via
        // KeyOrderEquals downstream) distinguishes reorder from membership shifts.
        code.AppendLineAt(
            6,
            "__bO" + id + " = __beforeOrder" + id + "; __aO" + id + " = __afterOrder" + id + ";"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
    }

    /// <summary>Emits Between-time sparse diff for a dictionary member (issue #103).</summary>
    private static void AppendDictBetweenSparse(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string escName,
        string runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var id = member.Id;
        var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
        var keyType = KeyTypeOf(member);
        var valueType = ValueTypeOf(member);
        var optValue = runtime + "Optional<" + valueType + ">";
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var facade = dialect.RuntimeFacade;
        var hasPatch = member.Collection.ValueType?.IsFragmentModel == true;
        var valueCs = hasPatch ? ValueChangeSetOf(member) : null;
        var valueFrag = hasPatch ? ValueFragmentOf(member) : null;
        ComputePublicNames(
            System.Collections.Immutable.ImmutableArray.Create(member),
            out _,
            out var transNames
        );
        var trans = transNames[member.Id];
        code.AppendLineAt(3, "bool __h" + id + " = false;");
        code.AppendLineAt(3, "bool __whole" + id + " = false;");
        code.AppendLineAt(3, opt + " __wb" + id + " = default;");
        code.AppendLineAt(3, opt + " __wa" + id + " = default;");
        code.AppendLineAt(
            3,
            "global::System.Collections.Generic.List<" + trans + ".Item>? __items" + id + " = null;"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var __before" + id + " = bf." + escName + ";");
        code.AppendLineAt(4, "var __after" + id + " = af." + escName + ";");
        code.AppendLineAt(
            4,
            "bool __beforeHas"
                + id
                + " = __before"
                + id
                + ".IsPresent && (object?)__before"
                + id
                + ".Value is not null;"
        );
        code.AppendLineAt(
            4,
            "bool __afterHas"
                + id
                + " = __after"
                + id
                + ".IsPresent && (object?)__after"
                + id
                + ".Value is not null;"
        );
        code.AppendLineAt(
            4,
            "if (__before"
                + id
                + ".IsPresent != __after"
                + id
                + ".IsPresent || !__beforeHas"
                + id
                + " || !__afterHas"
                + id
                + ")"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (!Fragment.__SparseEqual_" + id + "(__before" + id + ", __after" + id + "))"
        );
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "__h" + id + " = true; __whole" + id + " = true;");
        code.AppendLineAt(
            6,
            "__wb" + id + " = __before" + id + "; __wa" + id + " = __after" + id + ";"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "var __beforeDict"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">((global::System.Collections.Generic.IDictionary<"
                + keyType
                + ", "
                + valueType
                + ">)__before"
                + id
                + ".Value!, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            5,
            "var __afterDict"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">((global::System.Collections.Generic.IDictionary<"
                + keyType
                + ", "
                + valueType
                + ">)__after"
                + id
                + ".Value!, "
                + comparer
                + ");"
        );
        if (hasPatch)
        {
            code.AppendLineAt(
                5,
                "var __edited"
                    + id
                    + " = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueCs
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(
                5,
                "var __added"
                    + id
                    + " = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(
                5,
                "var __removed"
                    + id
                    + " = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(5, "foreach (var __k in __beforeDict" + id + ".Keys)");
            code.AppendLineAt(
                5,
                "{ if (!__afterDict"
                    + id
                    + ".ContainsKey(__k)) __removed"
                    + id
                    + "[__k] = __beforeDict"
                    + id
                    + "[__k]; }"
            );
            code.AppendLineAt(5, "foreach (var __kv in __afterDict" + id + ")");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "if (!__beforeDict"
                    + id
                    + ".TryGetValue(__kv.Key, out var __b)) { __added"
                    + id
                    + "[__kv.Key] = __kv.Value; continue; }"
            );
            code.AppendLineAt(
                6,
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
            code.AppendLineAt(6, "if (!__nested.IsEmpty) __edited" + id + "[__kv.Key] = __nested;");
            code.AppendLineAt(5, "}");
            code.AppendLineAt(
                5,
                "if (__added"
                    + id
                    + ".Count == 0 && __removed"
                    + id
                    + ".Count == 0 && __edited"
                    + id
                    + ".Count == 0) { }"
            );
            code.AppendLineAt(5, "else");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(6, "__h" + id + " = true;");
            code.AppendLineAt(
                6,
                "var __list"
                    + id
                    + " = new global::System.Collections.Generic.List<"
                    + trans
                    + ".Item>();"
            );
            code.AppendLineAt(
                6,
                "foreach (var __kv in __added"
                    + id
                    + ") { var __a = "
                    + optValue
                    + ".Present(__kv.Value); "
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
                    + ".Between(default, __ea); __list"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__kv.Key, default, __a, true, false, false, __edit, false)); }"
            );
            code.AppendLineAt(
                6,
                "foreach (var __kv in __removed"
                    + id
                    + ") { var __b = "
                    + optValue
                    + ".Present(__kv.Value); "
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
                    + ".Between(__eb, default); __list"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__kv.Key, __b, default, false, true, false, __edit, false)); }"
            );
            code.AppendLineAt(
                6,
                "foreach (var __kv in __edited"
                    + id
                    + ") { var __b = "
                    + optValue
                    + ".Present(__beforeDict"
                    + id
                    + "[__kv.Key]); var __a = "
                    + optValue
                    + ".Present(__afterDict"
                    + id
                    + "[__kv.Key]); __list"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__kv.Key, __b, __a, false, false, true, __kv.Value, false)); }"
            );
            code.AppendLineAt(6, "__items" + id + " = __list" + id + ";");
            code.AppendLineAt(5, "}");
        }
        else
        {
            code.AppendLineAt(
                5,
                "var __added"
                    + id
                    + " = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(
                5,
                "var __removed"
                    + id
                    + " = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(
                5,
                "var __edited"
                    + id
                    + " = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(5, "foreach (var __k in __beforeDict" + id + ".Keys)");
            code.AppendLineAt(
                5,
                "{ if (!__afterDict"
                    + id
                    + ".ContainsKey(__k)) __removed"
                    + id
                    + "[__k] = __beforeDict"
                    + id
                    + "[__k]; }"
            );
            code.AppendLineAt(5, "foreach (var __kv in __afterDict" + id + ")");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "if (!__beforeDict"
                    + id
                    + ".TryGetValue(__kv.Key, out var __b)) { __added"
                    + id
                    + "[__kv.Key] = __kv.Value; continue; }"
            );
            code.AppendLineAt(
                6,
                "if (!"
                    + facade
                    + ".AreEqual((object?)__b, (object?)__kv.Value)) __edited"
                    + id
                    + "[__kv.Key] = __kv.Value;"
            );
            code.AppendLineAt(5, "}");
            code.AppendLineAt(
                5,
                "if (__added"
                    + id
                    + ".Count == 0 && __removed"
                    + id
                    + ".Count == 0 && __edited"
                    + id
                    + ".Count == 0) { }"
            );
            code.AppendLineAt(5, "else");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(6, "__h" + id + " = true;");
            code.AppendLineAt(
                6,
                "var __list"
                    + id
                    + " = new global::System.Collections.Generic.List<"
                    + trans
                    + ".Item>();"
            );
            code.AppendLineAt(
                6,
                "foreach (var __kv in __added"
                    + id
                    + ") __list"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__kv.Key, default, "
                    + optValue
                    + ".Present(__kv.Value), true, false, false, false));"
            );
            code.AppendLineAt(
                6,
                "foreach (var __kv in __removed"
                    + id
                    + ") __list"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__kv.Key, "
                    + optValue
                    + ".Present(__kv.Value), default, false, true, false, false));"
            );
            code.AppendLineAt(
                6,
                "foreach (var __kv in __edited"
                    + id
                    + ") __list"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__kv.Key, "
                    + optValue
                    + ".Present(__beforeDict"
                    + id
                    + "[__kv.Key]), "
                    + optValue
                    + ".Present(__kv.Value), false, false, true, false));"
            );
            code.AppendLineAt(6, "__items" + id + " = __list" + id + ";");
            code.AppendLineAt(5, "}");
        }
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
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
