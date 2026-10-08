using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static SparseFragments.Generator.Shared.SparseChangeSetBetweenEmitter;
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

/// <summary>Canonical sparse field/type helpers plus ChangeSet storage, constructor, and emptiness.</summary>
internal static class SparseChangeSetBasicsEmitter
{
    internal static bool IsScalar(SparseMemberModel m) =>
        m.ChildModel is null && !SparseFragmentPatchEmitter.IsCollectionPatch(m);

    internal static bool IsNested(SparseMemberModel m) =>
        m.ChildModel is not null && !SparseFragmentPatchEmitter.IsCollectionPatch(m);

    internal static bool IsKeyed(SparseMemberModel m) =>
        SparseKeyedCollectionEmitter.IsKeyedSequence(m);

    internal static bool IsDict(SparseMemberModel m) =>
        SparseKeyedCollectionEmitter.IsDictionary(m);

    internal static string FragmentValueType(SparseMemberModel m) =>
        SparseFragmentEmitHelpers.FragmentValueType(m);

    internal static string ChildChangeSet(
        SparseMemberModel m,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    ) => dialect.ChildChangeSetName(m);

    internal static string BeforeField(SparseMemberModel m) => "__sparse_before_" + m.Id;

    internal static string AfterField(SparseMemberModel m) => "__sparse_after_" + m.Id;

    internal static string HasField(SparseMemberModel m) => "__sparse_has_" + m.Id;

    internal static string NestedField(SparseMemberModel m) => "__sparse_nested_" + m.Id;

    // Sparse keyed/dictionary canonical storage field names.
    // Changed keyed/dict members retain only the semantic transition
    // (whole presence transition OR granular per-key items + key-only orders),
    // never complete before/after member snapshots.
    internal static string KeyedWholeFlag(SparseMemberModel m) => "__sparse_kwhole_" + m.Id;

    internal static string KeyedWholeBefore(SparseMemberModel m) => "__sparse_kwholeBefore_" + m.Id;

    internal static string KeyedWholeAfter(SparseMemberModel m) => "__sparse_kwholeAfter_" + m.Id;

    internal static string KeyedItems(SparseMemberModel m) => "__sparse_kitems_" + m.Id;

    internal static string KeyedBeforeOrder(SparseMemberModel m) => "__sparse_kbeforeOrder_" + m.Id;

    internal static string KeyedAfterOrder(SparseMemberModel m) => "__sparse_kafterOrder_" + m.Id;

    internal static void AppendFields(
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
                // Canonical sparse storage. No full member snapshots.
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

    internal static void AppendConstructor(
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

    internal static string EmptyArgs(ImmutableArray<SparseMemberModel> members)
    {
        var parts = new List<string> { "false", "default", "default" };
        foreach (var member in members)
            parts.AddRange(EmptyMemberArgs(member));
        return string.Join(", ", parts);
    }

    internal static string[] EmptyMemberArgs(SparseMemberModel member)
    {
        if (IsNested(member))
            return ["null"];
        if (IsKeyed(member) || IsDict(member))
        {
            return IsKeyed(member)
                ? ["false", "false", "default", "default", "null", "null", "null"]
                : ["false", "false", "default", "default", "null"];
        }
        return ["default", "default", "false"];
    }

    internal static void AppendIsEmpty(
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

    internal static string MemberEmptyTail(ImmutableArray<SparseMemberModel> members)
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

    internal static string TransNameFor(
        ImmutableArray<SparseMemberModel> members,
        SparseMemberModel member
    )
    {
        ComputePublicNames(members, out _, out var transNames);
        return transNames[member.Id];
    }

    internal static string KeyTypeOf(SparseMemberModel m)
    {
        if (IsDict(m))
            return m.Collection.ElementType.Name;
        return m.Collection.KeyTypeName ?? "object?";
    }

    internal static string ElementTypeOf(SparseMemberModel m) => m.Collection.ElementType.Name;

    internal static string ValueTypeOf(SparseMemberModel m) =>
        m.Collection.ValueType?.Name ?? "object?";

    internal static string ElementChangeSetOf(SparseMemberModel m) =>
        m.Collection.ElementType.NonNullableName + ".ChangeSet";

    internal static string ElementFragmentOf(SparseMemberModel m) =>
        m.Collection.ElementType.NonNullableName + ".Fragment";

    internal static string ValueChangeSetOf(SparseMemberModel m) =>
        m.Collection.ValueType!.Value.NonNullableName + ".ChangeSet";

    internal static string ValueFragmentOf(SparseMemberModel m) =>
        m.Collection.ValueType!.Value.NonNullableName + ".Fragment";
}
