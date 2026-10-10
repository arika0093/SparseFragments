using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using static SparseFragments.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits the baseline-free payload projection for redacted transports.</summary>
/// <remarks>
/// A redacted payload can never rebuild a complete <c>ChangeSet</c>; this
/// projection spells the explicit information-loss boundary as a <c>Patch</c>.
/// Scalar members project their after-state, nested members recurse, and
/// keyed or dictionary members reuse the canonical member projection.
/// </remarks>
internal static class SparseChangeSetPayloadProjectionEmitter
{
    internal static void AppendCoreToPatch(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType
    )
    {
        if (modelType is null)
            throw new ArgumentException(
                "A payload projection requires a model type.",
                nameof(modelType)
            );
        SparseDownstreamPolicy.ThrowOnInvalidTransport(members, dialect);
        var runtime = dialect.RuntimeNamespace;
        var payloadRoot = SparseChangeSetPayloadEmitter.PayloadName(modelType, "Root");
        var rootChange = SparseChangeSetPayloadEmitter.PayloadName(modelType, "RootChange");
        var prefix = SparseNaming.PatchApiPrefix(
            members.Select(static member => member.Property.Name)
        );
        var strict =
            dialect.EffectiveRebasePolicy.RedactedBefore == SparseRedactedBeforeBehavior.StrictFail;
        code.AppendLineAt(
            2,
            "/// <summary>Projects this payload to a baseline-free patch.</summary>"
        );
        code.AppendLineAt(2, "/// <returns>The baseline-free patch.</returns>");
        code.AppendLineAt(2, "internal Patch ToPatchCore()");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (Changes is null) throw new global::System.ArgumentException(\"Payload changes must not be null.\", nameof(Changes));"
        );
        code.AppendLineAt(3, "if (Changes.Count == 1 && Changes[0] is " + rootChange + " whole)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (whole.Before is null || whole.After is null) throw new global::System.ArgumentException(\"A whole-root payload must contain both endpoints.\", nameof(Changes));"
        );
        if (strict && SparseDownstreamPolicy.HasAnyNonFullPolicy(dialect))
        {
            code.AppendLineAt(
                4,
                "throw new global::System.InvalidOperationException(\"A redacted payload cannot be projected under the strict rebase policy.\");"
            );
        }
        else
        {
            AppendWholeProjection(code, runtime, payloadRoot, prefix);
        }
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "var patch = new Patch();");
        code.AppendLineAt(3, "foreach (var change in Changes)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "switch (change)");
        code.AppendLineAt(4, "{");
        foreach (var member in members.Where(static member => !member.Property.IsJsonIgnored))
            AppendMemberProjection(code, member, members, dialect, modelType, strict);
        code.AppendLineAt(
            5,
            "default: throw new global::System.ArgumentException(\"Payload contains a null or unknown change variant.\", nameof(Changes));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return patch;");
        code.AppendLineAt(2, "}");
    }

    internal static void AppendRootToPatch(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType
    )
    {
        if (modelType is null)
            return;
        if (!SparseDownstreamPolicy.HasOwnNonFullPolicy(members, dialect))
            return;
        var versionLiteral = SymbolDisplay.FormatLiteral(dialect.ChangePayloadVersion, true);
        code.AppendLineAt(
            2,
            "/// <summary>Projects this payload to a baseline-free patch.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Redacted members project their requested after-state without historical comparison, unless the strict rebase policy applies.</remarks>"
        );
        code.AppendLineAt(2, "/// <returns>The baseline-free patch.</returns>");
        code.AppendLineAt(2, "public Patch ToPatch()");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (!global::System.String.Equals(Version, "
                + versionLiteral
                + ", global::System.StringComparison.Ordinal)) throw new global::System.ArgumentException(\"Unsupported ChangeSet payload version.\", nameof(Version));"
        );
        code.AppendLineAt(3, "return ToPatchCore();");
        code.AppendLineAt(2, "}");
    }

    private static void AppendWholeProjection(
        SharedIndentedBuilder code,
        string runtime,
        string payloadRoot,
        string prefix
    )
    {
        code.AppendLineAt(
            4,
            "var __patchBeforeRoot = whole.Before is null ? "
                + runtime
                + "Optional<"
                + payloadRoot
                + "?>.Missing : whole.Before.ToOptional();"
        );
        code.AppendLineAt(
            4,
            "var __patchAfterRoot = whole.After is null ? "
                + runtime
                + "Optional<"
                + payloadRoot
                + "?>.Missing : whole.After.ToOptional();"
        );
        code.AppendLineAt(
            4,
            runtime
                + "Optional<Fragment?> __patchBefore = !__patchBeforeRoot.IsPresent ? "
                + runtime
                + "Optional<Fragment?>.Missing : "
                + runtime
                + "Optional<Fragment?>.Present(__patchBeforeRoot.Value?.ToFragment());"
        );
        code.AppendLineAt(
            4,
            runtime
                + "Optional<Fragment?> __patchAfter = !__patchAfterRoot.IsPresent ? "
                + runtime
                + "Optional<Fragment?>.Missing : "
                + runtime
                + "Optional<Fragment?>.Present(__patchAfterRoot.Value?.ToFragment());"
        );
        code.AppendLineAt(4, "return Patch." + prefix + "Between(__patchBefore, __patchAfter);");
    }

    private static void AppendMemberProjection(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType,
        bool strict
    )
    {
        var id = member.Id;
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var variant = SparseChangeSetPayloadEmitter.PayloadMemberName(modelType, "Change", id);
        code.AppendLineAt(5, "case " + variant + " item:");
        if (IsNested(member))
        {
            code.AppendLineAt(
                6,
                "if (item.Nested is null) throw new global::System.ArgumentException(\"Nested payload is required.\", nameof(Changes));"
            );
            code.AppendLineAt(6, "patch." + name + " = item.Nested.ToPatchCore();");
            code.AppendLineAt(6, "break;");
            return;
        }

        if (IsKeyed(member) || IsDict(member))
        {
            AppendKeyedProjection(code, member, members, dialect, modelType, name);
            return;
        }

        var runtime = dialect.RuntimeNamespace;
        var valueType = FragmentValueType(member);
        var operation = runtime + "FragmentOperation<" + valueType + ">";
        if (dialect.GetTransport(member.Property.Name) == SparseMemberTransport.Full)
        {
            code.AppendLineAt(
                6,
                "if (item.Before is null || item.After is null) throw new global::System.ArgumentException(\"Both transition endpoints are required.\", nameof(Changes));"
            );
            code.AppendLineAt(6, "var __after" + id + " = item.After.ToOptional();");
            code.AppendLineAt(
                6,
                "patch."
                    + name
                    + " = __after"
                    + id
                    + ".IsPresent ? "
                    + operation
                    + ".Set(__after"
                    + id
                    + ".Value) : "
                    + operation
                    + ".Remove;"
            );
            code.AppendLineAt(6, "break;");
            return;
        }

        // Transport policy: project the requested after-state without
        // historical comparison, unless the strict policy refuses instead.
        code.AppendLineAt(
            6,
            "if (item.Before is not null) throw new global::System.ArgumentException(\"A redacted member must omit its before-state.\", nameof(Changes));"
        );
        code.AppendLineAt(
            6,
            "if (item.After is null) throw new global::System.ArgumentException(\"A redacted member requires an after-state.\", nameof(Changes));"
        );
        code.AppendLineAt(6, "var __redactedAfter" + id + " = item.After.ToOptional();");
        code.AppendLineAt(
            6,
            "if (!__redactedAfter"
                + id
                + ".IsPresent) throw new global::System.ArgumentException(\"A redacted member requires an after-state.\", nameof(Changes));"
        );
        if (strict)
        {
            code.AppendLineAt(
                6,
                "throw new global::System.InvalidOperationException(\"Member '"
                    + member.Property.Name
                    + "' is redacted and the strict rebase policy refuses projection.\");"
            );
        }
        else
        {
            code.AppendLineAt(
                6,
                "patch." + name + " = " + operation + ".Set(__redactedAfter" + id + ".Value);"
            );
        }
        code.AppendLineAt(6, "break;");
    }

    private static void AppendKeyedProjection(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType,
        string name
    )
    {
        var id = member.Id;
        var trans = TransNameFor(members, member);
        var runtime = dialect.RuntimeNamespace;
        var valueType = FragmentValueType(member);
        // Materialize canonical transition locals from the DTO, then reuse the
        // shared member projection so payload and ChangeSet paths cannot drift.
        code.AppendLineAt(
            6,
            "if (item.Items is null) throw new global::System.ArgumentException(\"Payload item changes must not be null.\", nameof(Changes));"
        );
        code.AppendLineAt(6, "bool " + HasField(member) + " = true;");
        code.AppendLineAt(
            6,
            "bool "
                + KeyedWholeFlag(member)
                + " = item.Before is not null || item.After is not null;"
        );
        code.AppendLineAt(
            6,
            runtime + "Optional<" + valueType + "> " + KeyedWholeBefore(member) + " = default;"
        );
        code.AppendLineAt(
            6,
            runtime + "Optional<" + valueType + "> " + KeyedWholeAfter(member) + " = default;"
        );
        code.AppendLineAt(
            6,
            "global::System.Collections.Generic.List<"
                + modelType
                + ".ChangeSet."
                + trans
                + ".Item>? "
                + KeyedItems(member)
                + " = null;"
        );
        if (IsKeyed(member))
        {
            var keyType = KeyTypeOf(member);
            code.AppendLineAt(
                6,
                "global::System.Collections.Generic.List<"
                    + keyType
                    + ">? "
                    + KeyedBeforeOrder(member)
                    + " = null;"
            );
            code.AppendLineAt(
                6,
                "global::System.Collections.Generic.List<"
                    + keyType
                    + ">? "
                    + KeyedAfterOrder(member)
                    + " = null;"
            );
        }
        code.AppendLineAt(6, "if (" + KeyedWholeFlag(member) + ")");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (item.Before is null || item.After is null || item.Items.Count != 0) throw new global::System.ArgumentException(\"A whole collection payload must contain both endpoints and no item changes.\", nameof(Changes));"
        );
        code.AppendLineAt(7, KeyedWholeBefore(member) + " = item.Before.ToOptional();");
        code.AppendLineAt(7, KeyedWholeAfter(member) + " = item.After.ToOptional();");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "else");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "var __payloadPatchItems"
                + id
                + " = new global::System.Collections.Generic.List<"
                + modelType
                + ".ChangeSet."
                + trans
                + ".Item>();"
        );
        code.AppendLineAt(7, "foreach (var __payloadPatchDto" + id + " in item.Items)");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(
            8,
            "if (__payloadPatchDto"
                + id
                + " is null) throw new global::System.ArgumentException(\"Payload item is required.\", nameof(Changes));"
        );
        code.AppendLineAt(
            8,
            "__payloadPatchItems"
                + id
                + ".Add("
                + modelType
                + ".ChangeSet.__SparsePayloadItem"
                + id
                + "(__payloadPatchDto"
                + id
                + "));"
        );
        code.AppendLineAt(7, "}");
        code.AppendLineAt(7, KeyedItems(member) + " = __payloadPatchItems" + id + ";");
        string? removalCount = null;
        if (IsKeyed(member))
        {
            code.AppendLineAt(7, KeyedBeforeOrder(member) + " = item.BeforeOrder;");
            code.AppendLineAt(7, KeyedAfterOrder(member) + " = item.AfterOrder;");
        }
        else
        {
            code.AppendLineAt(7, "int __payloadPatchRemovals" + id + " = 0;");
            code.AppendLineAt(
                7,
                "foreach (var __payloadPatchCount" + id + " in __payloadPatchItems" + id + ")"
            );
            code.AppendLineAt(
                8,
                "if (__payloadPatchCount" + id + ".IsRemoved) __payloadPatchRemovals" + id + "++;"
            );
            removalCount = "__payloadPatchRemovals" + id;
        }
        code.AppendLineAt(6, "}");
        SparseChangeSetPatchSyncEmitter.AppendKeyedDictToPatch(
            code,
            member,
            name,
            dialect,
            removalCount
        );
        code.AppendLineAt(6, "break;");
    }
}
