using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits the mixed-operation algebra for redacted/write-only payload members.</summary>
/// <remarks>
/// Transport adapter for issue #119, isolated from the complete ChangeSet algebra.
/// A member whose before-endpoint is redacted cannot form a baseline-aware
/// transition, so the partition below routes it to a baseline-free blind patch
/// while ordinary members keep the existing validation and rebase behavior.
/// Complete ChangeSet semantics stay untouched; every projection reports the
/// blind paths it handled without carrying secret values.
/// </remarks>
internal static class SparseChangeSetMixedEmitter
{
    internal static void AppendMixedPartition(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType
    )
    {
        var payloadCore = SparseChangeSetPayloadEmitter.PayloadName(modelType, "Core");
        var rootChange = SparseChangeSetPayloadEmitter.PayloadName(modelType, "RootChange");
        code.AppendLineAt(
            2,
            "/// <summary>Converts a validated root envelope to a change set.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <exception cref=\"global::System.ArgumentException\">Thrown when the payload carries redacted before-states, which cannot form baseline-aware transitions.</exception>"
        );
        code.AppendLineAt(
            2,
            "internal static ChangeSet FromPayloadCore(" + payloadCore + " payload)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (payload is null) throw new global::System.ArgumentNullException(nameof(payload));"
        );
        code.AppendLineAt(
            3,
            "var __changes = __SparseMixedPartition(payload, string.Empty, out var __blind, out var __blindPaths);"
        );
        code.AppendLineAt(
            3,
            "if (!__blind.__SparseIsEmpty()) throw new global::System.ArgumentException(\"The payload contains redacted before-states (\" + string.Join(\", \", __blindPaths) + \") and cannot form a baseline-aware ChangeSet. Use ToPatch() for the baseline-free projection, or InvertReversibleChanges()/TryApplyMixedTo() for mixed requests.\", nameof(payload));"
        );
        code.AppendLineAt(3, "return __changes;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Splits a validated envelope into baseline-aware transitions plus a blind patch.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Redacted before-states route to the blind patch without historical comparison; ordinary members keep baseline-aware validation. After-states must stay concrete.</remarks>"
        );
        code.AppendLineAt(
            2,
            "internal static ChangeSet __SparseMixedPartition("
                + payloadCore
                + " payload, string pathPrefix, out Patch blindSets, out global::System.Collections.Generic.List<string> blindPaths)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (payload is null) throw new global::System.ArgumentNullException(nameof(payload));"
        );
        code.AppendLineAt(
            3,
            "if (pathPrefix is null) throw new global::System.ArgumentNullException(nameof(pathPrefix));"
        );
        code.AppendLineAt(3, "blindSets = new Patch();");
        code.AppendLineAt(3, "blindPaths = new global::System.Collections.Generic.List<string>();");
        code.AppendLineAt(
            3,
            "if (payload.Changes is null) throw new global::System.ArgumentException(\"Payload changes must not be null.\", nameof(payload));"
        );
        AppendWholePartition(code, members, modelType, rootChange);
        foreach (var member in members.Where(static member => !member.Property.IsJsonIgnored))
        {
            AppendPartitionLocals(code, member, dialect, members);
        }
        foreach (var member in members.Where(static member => !member.Property.IsJsonIgnored))
        {
            code.AppendLineAt(3, "bool __payloadSeen" + member.Id + " = false;");
        }
        code.AppendLineAt(3, "foreach (var change in payload.Changes)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "switch (change)");
        code.AppendLineAt(4, "{");
        foreach (var member in members.Where(static member => !member.Property.IsJsonIgnored))
        {
            AppendPartitionCase(code, member, dialect, modelType);
        }
        code.AppendLineAt(
            5,
            "default: throw new global::System.ArgumentException(\"Payload contains a null or unknown change variant.\", nameof(payload));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        var args = new List<string> { "false", "default", "default" };
        foreach (var member in members)
        {
            var id = member.Id;
            if (member.Property.IsJsonIgnored)
            {
                args.AddRange(SparseChangeSetBasicsEmitter.EmptyMemberArgs(member));
                continue;
            }
            if (SparseChangeSetBasicsEmitter.IsNested(member))
                args.Add("__payloadNested" + id);
            else if (
                SparseChangeSetBasicsEmitter.IsKeyed(member)
                || SparseChangeSetBasicsEmitter.IsDict(member)
            )
            {
                args.AddRange(
                    new[]
                    {
                        "__payloadHas" + id,
                        "__payloadWhole" + id,
                        "__payloadBefore" + id,
                        "__payloadAfter" + id,
                        "__payloadItems" + id,
                    }
                );
                if (SparseChangeSetBasicsEmitter.IsKeyed(member))
                    args.AddRange(
                        new[] { "__payloadBeforeOrder" + id, "__payloadAfterOrder" + id }
                    );
            }
            else
                args.AddRange(
                    new[] { "__payloadBefore" + id, "__payloadAfter" + id, "__payloadHas" + id }
                );
        }
        code.AppendLineAt(3, "return new ChangeSet(" + string.Join(", ", args) + ");");
        code.AppendLineAt(2, "}");
    }

    private static void AppendPartitionLocals(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        ImmutableArray<SparseMemberModel> members
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var id = member.Id;
        if (SparseChangeSetBasicsEmitter.IsNested(member))
        {
            code.AppendLineAt(
                3,
                SparseChangeSetBasicsEmitter.ChildChangeSet(member, dialect)
                    + "? __payloadNested"
                    + id
                    + " = null;"
            );
            return;
        }
        if (
            SparseChangeSetBasicsEmitter.IsKeyed(member)
            || SparseChangeSetBasicsEmitter.IsDict(member)
        )
        {
            var opt =
                runtime
                + "Optional<"
                + SparseChangeSetBasicsEmitter.FragmentValueType(member)
                + ">";
            var trans = SparseChangeSetBasicsEmitter.TransNameFor(members, member);
            code.AppendLineAt(3, "bool __payloadHas" + id + " = false;");
            code.AppendLineAt(3, "bool __payloadWhole" + id + " = false;");
            code.AppendLineAt(3, opt + " __payloadBefore" + id + " = default;");
            code.AppendLineAt(3, opt + " __payloadAfter" + id + " = default;");
            code.AppendLineAt(
                3,
                "var __payloadItems"
                    + id
                    + " = new global::System.Collections.Generic.List<"
                    + trans
                    + ".Item>();"
            );
            if (SparseChangeSetBasicsEmitter.IsKeyed(member))
            {
                var keyType = SparseChangeSetBasicsEmitter.KeyTypeOf(member);
                code.AppendLineAt(
                    3,
                    "global::System.Collections.Generic.List<"
                        + keyType
                        + ">? __payloadBeforeOrder"
                        + id
                        + " = null;"
                );
                code.AppendLineAt(
                    3,
                    "global::System.Collections.Generic.List<"
                        + keyType
                        + ">? __payloadAfterOrder"
                        + id
                        + " = null;"
                );
            }
            return;
        }
        var scalarOpt =
            runtime + "Optional<" + SparseChangeSetBasicsEmitter.FragmentValueType(member) + ">";
        code.AppendLineAt(3, "bool __payloadHas" + id + " = false;");
        code.AppendLineAt(3, scalarOpt + " __payloadBefore" + id + " = default;");
        code.AppendLineAt(3, scalarOpt + " __payloadAfter" + id + " = default;");
    }

    private static void AppendPartitionCase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType
    )
    {
        var id = member.Id;
        var runtime = dialect.RuntimeNamespace;
        var prop = member.Property.Name;
        var propLit = SymbolDisplay.FormatLiteral(prop, true);
        var path = "pathPrefix + " + propLit;
        var variant = SparseChangeSetPayloadEmitter.PayloadName(modelType, "Change") + id;
        var esc = SparseNaming.EscapeIdentifier(prop);
        code.AppendLineAt(5, "case " + variant + " item:");
        code.AppendLineAt(
            6,
            "if (__payloadSeen"
                + id
                + ") throw new global::System.ArgumentException(\"A payload cannot contain duplicate member changes.\", nameof(payload));"
        );
        code.AppendLineAt(6, "__payloadSeen" + id + " = true;");
        if (SparseChangeSetBasicsEmitter.IsNested(member))
        {
            var childChangeSet = SparseChangeSetBasicsEmitter.ChildChangeSet(member, dialect);
            code.AppendLineAt(
                6,
                "if (item.Nested is null) throw new global::System.ArgumentException(\"Nested payload is required.\", nameof(payload));"
            );
            code.AppendLineAt(
                6,
                "var __nested"
                    + id
                    + " = "
                    + childChangeSet
                    + ".__SparseMixedPartition(item.Nested, "
                    + path
                    + " + \".\", out var __nestedBlind"
                    + id
                    + ", out var __nestedPaths"
                    + id
                    + ");"
            );
            code.AppendLineAt(6, "__payloadNested" + id + " = __nested" + id + ";");
            code.AppendLineAt(
                6,
                "if (!__nestedBlind"
                    + id
                    + ".__SparseIsEmpty()) blindSets."
                    + esc
                    + " = __nestedBlind"
                    + id
                    + ";"
            );
            code.AppendLineAt(6, "blindPaths.AddRange(__nestedPaths" + id + ");");
            code.AppendLineAt(6, "break;");
            return;
        }
        if (
            SparseChangeSetBasicsEmitter.IsKeyed(member)
            || SparseChangeSetBasicsEmitter.IsDict(member)
        )
        {
            AppendKeyedPartitionCase(code, member, dialect, id, esc, path);
            return;
        }
        var operation =
            runtime
            + "FragmentOperation<"
            + SparseFragmentPatchEmitter.GetMemberValueType(dialect, member)
            + ">";
        code.AppendLineAt(
            6,
            "if (item.Before is null || item.After is null) throw new global::System.ArgumentException(\"Both transition endpoints are required.\", nameof(payload));"
        );
        code.AppendLineAt(
            6,
            "if (item.After.IsRedacted) throw new global::System.ArgumentException(\"The payload contains a redacted after-state for '\" + "
                + path
                + " + \"'. After-states must stay concrete (value, null, or missing).\", nameof(payload));"
        );
        code.AppendLineAt(6, "if (item.Before.IsRedacted)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(6, "var __blindAfter" + id + " = item.After.ToOptional();");
        code.AppendLineAt(
            7,
            "blindSets."
                + esc
                + " = __blindAfter"
                + id
                + ".IsPresent ? "
                + operation
                + ".Set(__blindAfter"
                + id
                + ".Value) : "
                + operation
                + ".Remove;"
        );
        code.AppendLineAt(6, "blindPaths.Add(" + path + ");");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "else");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(6, "__payloadHas" + id + " = true;");
        code.AppendLineAt(6, "__payloadBefore" + id + " = item.Before.ToOptional();");
        code.AppendLineAt(6, "__payloadAfter" + id + " = item.After.ToOptional();");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "break;");
    }

    private static void AppendKeyedPartitionCase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        int id,
        string esc,
        string path
    )
    {
        var isKeyed = SparseChangeSetBasicsEmitter.IsKeyed(member);
        var keyType = SparseChangeSetBasicsEmitter.KeyTypeOf(member);
        var collectionPatch =
            "Patch." + SparseFragmentPatchEmitter.GetCollectionPatchName(dialect, member);
        code.AppendLineAt(6, "__payloadHas" + id + " = true;");
        code.AppendLineAt(
            6,
            "if (item.Items is null) throw new global::System.ArgumentException(\"Payload item changes must not be null.\", nameof(payload));"
        );
        // Per-item redacted endpoints cannot form item transitions; fail before
        // the shared item helper converts endpoints without naming the path.
        code.AppendLineAt(6, "foreach (var __raw" + id + " in item.Items)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (__raw"
                + id
                + " is null) throw new global::System.ArgumentException(\"Payload item is required.\", nameof(payload));"
        );
        code.AppendLineAt(
            7,
            "if (__raw"
                + id
                + ".Before is not null && __raw"
                + id
                + ".Before.IsRedacted) throw new global::System.ArgumentException(\"The payload contains a redacted item before-state for '\" + "
                + path
                + " + \"[\" + (((object?)__raw"
                + id
                + ".Key is null) ? \"?\" : (object?)__raw"
                + id
                + ".Key) + \"]\" + \"'. Per-item redacted endpoints are not supported; send a whole-member blind set.\", nameof(payload));"
        );
        code.AppendLineAt(
            7,
            "if (__raw"
                + id
                + ".After is not null && __raw"
                + id
                + ".After.IsRedacted) throw new global::System.ArgumentException(\"The payload contains a redacted item after-state for '\" + "
                + path
                + " + \"[\" + (((object?)__raw"
                + id
                + ".Key is null) ? \"?\" : (object?)__raw"
                + id
                + ".Key) + \"]\" + \"'. After-states must stay concrete (value, null, or missing).\", nameof(payload));"
        );
        code.AppendLineAt(6, "}");
        code.AppendLineAt(
            6,
            "if (item.After is not null && item.After.IsRedacted) throw new global::System.ArgumentException(\"The payload contains a redacted after-state for '\" + "
                + path
                + " + \"'. After-states must stay concrete (value, null, or missing).\", nameof(payload));"
        );
        code.AppendLineAt(6, "if (item.Before is not null || item.After is not null)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (item.Before is null || item.After is null || item.Items.Count != 0) throw new global::System.ArgumentException(\"A whole collection payload must contain both endpoints and no item changes.\", nameof(payload));"
        );
        code.AppendLineAt(7, "if (item.Before.IsRedacted)");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(7, "var __wholeAfter" + id + " = item.After.ToOptional();");
        code.AppendLineAt(
            8,
            "if (!__wholeAfter"
                + id
                + ".IsPresent) throw new global::System.ArgumentException(\"The payload redacts the before-state of whole collection member '\" + "
                + path
                + " + \"' with a missing after-state. Blind whole-collection removal has no patch projection; express the removal with an explicit patch.\", nameof(payload));"
        );
        code.AppendLineAt(8, "var __wholeBlind" + id + " = new " + collectionPatch + "();");
        code.AppendLineAt(8, "__wholeBlind" + id + ".Set(__wholeAfter" + id + ".Value!);");
        code.AppendLineAt(8, "blindSets." + esc + " = __wholeBlind" + id + ";");
        code.AppendLineAt(8, "blindPaths.Add(" + path + ");");
        code.AppendLineAt(8, "__payloadHas" + id + " = false;");
        code.AppendLineAt(7, "}");
        code.AppendLineAt(7, "else");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(8, "__payloadWhole" + id + " = true;");
        code.AppendLineAt(8, "__payloadBefore" + id + " = item.Before.ToOptional();");
        code.AppendLineAt(8, "__payloadAfter" + id + " = item.After.ToOptional();");
        code.AppendLineAt(7, "}");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "else");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "var __payloadSeenKeys"
                + id
                + " = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">();"
        );
        code.AppendLineAt(7, "foreach (var changeItem in item.Items)");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(
            8,
            "if (changeItem is null) throw new global::System.ArgumentException(\"Payload item is required.\", nameof(payload));"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLineAt(
                8,
                "if (!"
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "changeItem.Key")
                    + " && !__payloadSeenKeys"
                    + id
                    + ".Add(changeItem.Key)) throw new global::System.ArgumentException(\"A payload cannot contain duplicate keyed changes.\", nameof(payload));"
            );
        }
        else
        {
            code.AppendLineAt(
                8,
                "if (!__payloadSeenKeys"
                    + id
                    + ".Add(changeItem.Key)) throw new global::System.ArgumentException(\"A payload cannot contain duplicate keyed changes.\", nameof(payload));"
            );
        }
        code.AppendLineAt(
            8,
            "__payloadItems" + id + ".Add(__SparsePayloadItem" + id + "(changeItem));"
        );
        code.AppendLineAt(7, "}");
        if (isKeyed)
        {
            code.AppendLineAt(7, "__payloadBeforeOrder" + id + " = item.BeforeOrder;");
            code.AppendLineAt(7, "__payloadAfterOrder" + id + " = item.AfterOrder;");
        }
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "break;");
    }

    private static void AppendWholePartition(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string? modelType,
        string rootChange
    )
    {
        code.AppendLineAt(
            3,
            "if (payload.Changes.Count == 1 && payload.Changes[0] is " + rootChange + " whole)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (whole.Before is null || whole.After is null) throw new global::System.ArgumentException(\"A whole-root payload must contain both endpoints.\", nameof(payload));"
        );
        code.AppendLineAt(
            4,
            "if (whole.After.IsRedacted) throw new global::System.ArgumentException(\"The payload contains a redacted after-state for '\" + pathPrefix + \"$root\" + \"'. After-states must stay concrete (value, null, or missing).\", nameof(payload));"
        );
        code.AppendLineAt(4, "if (whole.Before.IsRedacted)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(4, "var __wholeAfter = whole.After.ToOptional();");
        code.AppendLineAt(4, "if (!__wholeAfter.IsPresent) blindSets.__SparseRemove();");
        code.AppendLineAt(4, "else if (__wholeAfter.Value is null) blindSets.__SparseSetNull();");
        if (modelType is null)
        {
            code.AppendLineAt(
                5,
                "else throw new global::System.ArgumentException(\"A whole-root blind set requires a model-typed envelope.\", nameof(payload));"
            );
        }
        else
        {
            code.AppendLineAt(
                5,
                "else blindSets.__SparseSet(__wholeAfter.Value.ToFragment().ToModel());"
            );
        }
        code.AppendLineAt(4, "blindPaths.Add(pathPrefix + \"$root\");");
        code.AppendLineAt(
            4,
            "return new ChangeSet(false, default, default, "
                + SparseChangeSetBasicsEmitter.MemberEmptyTail(members)
                + ");"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "return new ChangeSet(true, __SparsePayloadFragment(whole.Before), __SparsePayloadFragment(whole.After), "
                + SparseChangeSetBasicsEmitter.MemberEmptyTail(members)
                + ");"
        );
        code.AppendLineAt(3, "}");
    }

    internal static void AppendMixedPayloadSurface(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        ImmutableArray<string> ignoredSettablePropertyNames
    )
    {
        var prefix = SparseNaming.PatchApiPrefix(
            members.Select(static member => member.Property.Name)
        );
        code.AppendLineAt(
            2,
            "/// <summary>Discards baseline information and returns the equivalent desired-operation patch.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Redacted before-states project to their requested after-state without historical comparison; ordinary members project their after-state too. The result is baseline-free and can no longer rebase or report conflicts.</remarks>"
        );
        code.AppendLineAt(2, "public Patch ToPatch()");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "var __changes = ChangeSet.__SparseMixedPartition(this, string.Empty, out var __blind, out _);"
        );
        code.AppendLineAt(3, "return __changes.ToPatch()." + prefix + "Compose(__blind);");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Inverts the reversible transitions and reports write-only paths.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Write-only operations have no prior value to restore, so they are excluded. A non-empty <paramref name=\"skippedPaths\"/> means the result is not a complete inverse; use <c>ChangeSet.Invert()</c> on complete change sets for true inversion.</remarks>"
        );
        code.AppendLineAt(
            2,
            "public ChangeSet InvertReversibleChanges(out global::System.Collections.Generic.IReadOnlyList<string> skippedPaths)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "var __changes = ChangeSet.__SparseMixedPartition(this, string.Empty, out _, out var __blindPaths);"
        );
        code.AppendLineAt(3, "skippedPaths = __blindPaths.ToArray();");
        code.AppendLineAt(3, "return __changes.Invert();");
        code.AppendLineAt(2, "}");
        if (modelType is null)
        {
            return;
        }
        AppendMixedApplyOutcome(code, dialect);
        AppendTryApplyMixed(
            code,
            members,
            dialect,
            modelType,
            prefix,
            ignoredSettablePropertyNames
        );
    }

    private static void AppendMixedApplyOutcome(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var conflict = dialect.ConflictType;
        code.AppendLineAt(
            2,
            "/// <summary>The outcome of applying a mixed request to an ordinary model.</summary>"
        );
        code.AppendLineAt(2, "public sealed class MixedApplyResult");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "internal MixedApplyResult(global::System.Collections.Generic.IReadOnlyList<"
                + conflict
                + "> conflicts, global::System.Collections.Generic.IReadOnlyList<string> writeOnlyPaths)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "Conflicts = conflicts;");
        code.AppendLineAt(4, "WriteOnlyPaths = writeOnlyPaths;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "internal static MixedApplyResult Applied(global::System.Collections.Generic.List<string> writeOnlyPaths) => new(global::System.Array.Empty<"
                + conflict
                + ">(), writeOnlyPaths.ToArray());"
        );
        code.AppendLineAt(
            3,
            "internal static MixedApplyResult Conflicted(global::System.Collections.Generic.IReadOnlyList<"
                + conflict
                + "> conflicts, global::System.Collections.Generic.List<string> writeOnlyPaths) => new(conflicts, writeOnlyPaths.ToArray());"
        );
        code.AppendLineAt(
            3,
            "/// <summary>Whether rebasing the baseline-aware members produced conflicts.</summary>"
        );
        code.AppendLineAt(3, "public bool HasConflicts => Conflicts.Count != 0;");
        code.AppendLineAt(
            3,
            "/// <summary>Structured conflicts for baseline-aware members only.</summary>"
        );
        code.AppendLineAt(
            3,
            "public global::System.Collections.Generic.IReadOnlyList<"
                + conflict
                + "> Conflicts { get; }"
        );
        code.AppendLineAt(
            3,
            "/// <summary>Write-only member paths applied without baseline comparison.</summary>"
        );
        code.AppendLineAt(
            3,
            "public global::System.Collections.Generic.IReadOnlyList<string> WriteOnlyPaths { get; }"
        );
        code.AppendLineAt(2, "}");
    }

    private static void AppendTryApplyMixed(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType,
        string prefix,
        ImmutableArray<string> ignoredSettablePropertyNames
    )
    {
        _ = members;
        var runtime = dialect.RuntimeNamespace;
        var optionalFragment = runtime + "Optional<Fragment?>";
        code.AppendLineAt(
            2,
            "/// <summary>Applies a mixed request to an ordinary model without committing a subset on conflict.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Baseline-aware members rebase onto the current model; redacted-before members pass their requested after-state through. When rebasing conflicts, nothing is applied and the current model is left untouched. This default passes write-only operations through; it does not claim conflict reconciliation, and revision checks stay with the application.</remarks>"
        );
        code.AppendLineAt(
            2,
            "public bool TryApplyMixedTo("
                + modelType
                + " current, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out "
                + modelType
                + "? updated, out MixedApplyResult result)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "var __changes = ChangeSet.__SparseMixedPartition(this, string.Empty, out var __blind, out var __blindPaths);"
        );
        code.AppendLineAt(
            3,
            "var __state = " + optionalFragment + ".Present(Fragment.From(current));"
        );
        code.AppendLineAt(3, "ChangeSet __toApply;");
        code.AppendLineAt(3, "if (__changes.__SparseBeforeMatches(__state))");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "__toApply = __changes;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "else");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var __rebase = __changes.RebaseOnto(__state);");
        code.AppendLineAt(4, "if (__rebase.HasConflicts)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "updated = null;");
        code.AppendLineAt(
            5,
            "result = MixedApplyResult.Conflicted(__rebase.Conflicts, __blindPaths);"
        );
        code.AppendLineAt(5, "return false;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "__toApply = __rebase.Rebased;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "var __applied = __toApply.ToPatch()." + prefix + "Compose(__blind).Apply(__state);"
        );
        code.AppendLineAt(
            3,
            "if (!__applied.IsPresent || __applied.Value is null) throw new global::System.InvalidOperationException(\"Advancing the baseline did not produce a valid model state.\");"
        );
        code.AppendLineAt(3, "var __updatedModel = __applied.Value.ToModel();");
        foreach (
            var ignoredName in ignoredSettablePropertyNames.IsDefault
                ? ImmutableArray<string>.Empty
                : ignoredSettablePropertyNames
        )
        {
            var name = SparseNaming.EscapeIdentifier(ignoredName);
            code.AppendLineAt(3, "__updatedModel." + name + " = current." + name + ";");
        }
        code.AppendLineAt(3, "updated = __updatedModel;");
        code.AppendLineAt(3, "result = MixedApplyResult.Applied(__blindPaths);");
        code.AppendLineAt(3, "return true;");
        code.AppendLineAt(2, "}");
        _ = runtime;
    }
}
