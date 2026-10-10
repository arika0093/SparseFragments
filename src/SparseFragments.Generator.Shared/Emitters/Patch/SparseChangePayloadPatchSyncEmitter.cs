using System.Collections.Immutable;
using System.Linq;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits ChangePayload patch synchronization for one model.</summary>
/// <remarks>A ChangePayload carries baseline-aware transitions and baseline-free commands side by side. These helpers build the command core from a patch and project any core back to a patch without ever fabricating a baseline.</remarks>
internal static class SparseChangePayloadPatchSyncEmitter
{
    /// <summary>Emits the patch-side command core builder inside <c>Patch</c>.</summary>
    /// <remarks>Runs inside Patch so collection and operation internals stay accessible without widening their visibility.</remarks>
    internal static void AppendPatchToPayloadCore(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        string? implementationNamespace = null,
        SparseOperationTarget? target = null
    )
    {
        if (modelType is null)
            return;
        if (target is not null)
        {
            var core = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "Core");
            code.AppendLineAt(
                2,
                "/// <summary>Builds a baseline-free command core from this patch.</summary>"
            );
            code.AppendLineAt(
                2,
                "/// <remarks>Before-states are redacted by construction; the result only supports <see cref=\"ChangePayload.ToPatch\"/>. JSON-ignored members throw instead of exporting lossy cores.</remarks>"
            );
            code.AppendLineAt(2, "/// <returns>The baseline-free command core.</returns>");
            code.AppendLineAt(
                2,
                "internal "
                    + core
                    + " ToChangePayloadCore() => "
                    + target.PatchOperationsType
                    + ".ToChangePayloadCore(this);"
            );
            code = target.PatchOperations;
        }
        AppendCoreFromPatch(code, members, dialect, modelType, implementationNamespace, target);
    }

    /// <summary>Emits the core-to-patch projection inside <c>ChangeSet</c>.</summary>
    internal static void AppendPatchFromCore(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        SparseOperationTarget? target = null
    )
    {
        if (modelType is null)
            return;
        if (target is not null)
        {
            var core = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "Core");
            // The payload DTO bridges keep calling through the facade.
            code.AppendLineAt(
                2,
                "/// <summary>Projects a validated command core to a baseline-free patch.</summary>"
            );
            code.AppendLineAt(2, "/// <param name=\"payload\">The validated command core.</param>");
            code.AppendLineAt(2, "/// <returns>The baseline-free patch.</returns>");
            code.AppendLineAt(
                2,
                "internal static Patch PatchFromPayloadCore("
                    + core
                    + " payload) => "
                    + target.ChangeSetOperationsType
                    + ".PatchFromPayloadCore(payload);"
            );
            code = target.ChangeSetOperations;
        }
        AppendPatchFromCoreMethod(code, members, dialect, modelType);
    }

    private static void AppendCoreFromPatch(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType,
        string? implementationNamespace = null,
        SparseOperationTarget? target = null
    )
    {
        var core = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "Core");
        var change = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "Change");
        code.AppendLineAt(
            2,
            "/// <summary>Builds a baseline-free command core from this patch.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Before-states are redacted by construction; the result only supports <see cref=\"ChangePayload.ToPatch\"/>. JSON-ignored members throw instead of exporting lossy cores.</remarks>"
        );
        if (target is not null)
            code.AppendLineAt(2, "/// <param name=\"self\">The patch to convert.</param>");
        code.AppendLineAt(2, "/// <returns>The baseline-free command core.</returns>");
        code.AppendLineAt(
            2,
            (target is null ? "internal " : "internal static ")
                + core
                + " ToChangePayloadCore("
                + (target is null ? string.Empty : "Patch self")
                + ")"
        );
        code.AppendLineAt(2, "{");
        if (target is not null)
        {
            SparseFragmentPatchCoreEmitter.AppendPatchSelfAliases(code, members, dialect);
        }
        AppendIgnoredPatchGuard(code, members, dialect);
        code.AppendLineAt(
            3,
            "var changes = new global::System.Collections.Generic.List<" + change + ">();"
        );
        foreach (var member in members.Where(static member => !member.Property.IsJsonIgnored))
            AppendCoreFromPatchMember(code, member, dialect, modelType, implementationNamespace);
        code.AppendLineAt(3, "return new " + core + " { Changes = changes };");
        code.AppendLineAt(2, "}");
    }

    /// <summary>Emits the fail-closed guard for JSON-ignored members in transport cores.</summary>
    /// <remarks>
    /// Transport excludes STJ <c>JsonIgnore</c> members, so exporting a change
    /// that touches one would silently lose it (issue #165). The guard names
    /// the omitted paths instead, keeping <c>ToPayload</c> lossless while
    /// ordinary <c>Fragment</c> JSON keeps honoring <c>JsonIgnore</c>.
    /// </remarks>
    internal static void AppendIgnoredTransportGuard(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members
    )
    {
        var ignored = members.Where(static member => member.Property.IsJsonIgnored).ToArray();
        if (ignored.Length == 0)
        {
            return;
        }

        code.AppendLineAt(
            3,
            "var __ignoredOmitted = new global::System.Collections.Generic.List<string>();"
        );
        code.AppendLineAt(3, "if (__sparse_hasWhole) __ignoredOmitted.Add(\"$root\");");
        foreach (var member in ignored)
        {
            var literal = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(
                member.Property.Name,
                true
            );
            if (SparseChangeSetBasicsEmitter.IsNested(member))
            {
                code.AppendLineAt(
                    3,
                    "if ("
                        + SparseChangeSetBasicsEmitter.NestedField(member)
                        + " is not null) __ignoredOmitted.Add("
                        + literal
                        + ");"
                );
            }
            else
            {
                code.AppendLineAt(
                    3,
                    "if ("
                        + SparseChangeSetBasicsEmitter.HasField(member)
                        + ") __ignoredOmitted.Add("
                        + literal
                        + ");"
                );
            }
        }
        code.AppendLineAt(
            3,
            "if (__ignoredOmitted.Count != 0) throw new global::System.InvalidOperationException(\"ChangeSet transport omits JSON-ignored members (\" + string.Join(\", \", __ignoredOmitted) + \").\");"
        );
    }

    /// <summary>Emits the fail-closed guard for JSON-ignored patch fields.</summary>
    /// <remarks>
    /// Patch transport excludes STJ <c>JsonIgnore</c> members, so a patch that
    /// sets one cannot export losslessly (issue #165). Runs inside
    /// <c>Patch</c>, where collection and operation internals are visible.
    /// </remarks>
    private static void AppendIgnoredPatchGuard(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var ignored = members.Where(static member => member.Property.IsJsonIgnored).ToArray();
        if (ignored.Length == 0)
        {
            return;
        }

        var kind = dialect.RuntimeNamespace + "FragmentOperationKind";
        code.AppendLineAt(
            3,
            "var __ignoredOmitted = new global::System.Collections.Generic.List<string>();"
        );
        foreach (var member in ignored)
        {
            var literal = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(
                member.Property.Name,
                true
            );
            var field = dialect.MemberField(member);
            if (SparseChangeSetBasicsEmitter.IsNested(member))
            {
                code.AppendLineAt(
                    3,
                    "if ("
                        + field
                        + " is not null && !"
                        + field
                        + ".__SparseIsEmpty()) __ignoredOmitted.Add("
                        + literal
                        + ");"
                );
            }
            else if (
                SparseChangeSetBasicsEmitter.IsKeyed(member)
                || SparseChangeSetBasicsEmitter.IsDict(member)
            )
            {
                code.AppendLineAt(
                    3,
                    "if ("
                        + field
                        + " is not null && !"
                        + field
                        + ".__SparseIsEmpty()) __ignoredOmitted.Add("
                        + literal
                        + ");"
                );
            }
            else
            {
                code.AppendLineAt(
                    3,
                    "if ("
                        + field
                        + ".Kind != "
                        + kind
                        + ".Keep) __ignoredOmitted.Add("
                        + literal
                        + ");"
                );
            }
        }
        code.AppendLineAt(
            3,
            "if (__ignoredOmitted.Count != 0) throw new global::System.InvalidOperationException(\"ChangeSet transport omits JSON-ignored members (\" + string.Join(\", \", __ignoredOmitted) + \").\");"
        );
    }

    private static void AppendCoreFromPatchMember(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType,
        string? implementationNamespace = null
    )
    {
        var id = member.Id;
        var runtime = dialect.RuntimeNamespace;
        var endpoint = runtime + "ChangePayloadEndpoint";
        var state = runtime + "ChangePayloadState";
        var kind = runtime + "FragmentOperationKind";
        var change = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(
            dialect,
            modelType,
            "Change",
            id
        );
        var field = dialect.MemberField(member);
        if (SparseChangeSetBasicsEmitter.IsNested(member))
        {
            var childCore = SparseChangeSetPayloadEmitter.QualifiedChildPayloadType(
                dialect,
                member.ChildModel!.Value.NonNullableName,
                "Core",
                implementationNamespace
            );
            code.AppendLineAt(3, "{");
            code.AppendLineAt(3, "var nestedPatch" + id + " = " + field + ";");
            code.AppendLineAt(
                4,
                "if (nestedPatch"
                    + id
                    + " is not null && !nestedPatch"
                    + id
                    + ".__SparseIsEmpty()) changes.Add(new "
                    + change
                    + " { Nested = "
                    + childCore
                    + ".FromPatchCore(nestedPatch"
                    + id
                    + ") });"
            );
            code.AppendLineAt(3, "}");
        }
        else if (
            SparseChangeSetBasicsEmitter.IsKeyed(member)
            || SparseChangeSetBasicsEmitter.IsDict(member)
        )
        {
            AppendCoreFromPatchCollection(code, dialect, modelType, id, field);
        }
        else
        {
            var valueType = SparseChangeSetBasicsEmitter.FragmentValueType(member);
            code.AppendLineAt(3, "{");
            code.AppendLineAt(3, "var operation" + id + " = " + field + ";");
            code.AppendLineAt(3, "if (operation" + id + ".Kind != " + kind + ".Keep)");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "var entry" + id + " = new " + change + "();");
            code.AppendLineAt(
                4,
                "entry" + id + ".Before = " + endpoint + "<" + valueType + ">.Redacted();"
            );
            code.AppendLineAt(
                4,
                "entry"
                    + id
                    + ".After = operation"
                    + id
                    + ".Kind == "
                    + kind
                    + ".Set ? "
                    + endpoint
                    + "<"
                    + valueType
                    + ">.FromOptional("
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(operation"
                    + id
                    + ".Value)) : new "
                    + endpoint
                    + "<"
                    + valueType
                    + "> { State = "
                    + state
                    + ".Missing };"
            );
            code.AppendLineAt(4, "changes.Add(entry" + id + ");");
            code.AppendLineAt(3, "}");
            code.AppendLineAt(3, "}");
        }
    }

    private static void AppendCoreFromPatchCollection(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType,
        int id,
        string field
    )
    {
        var change = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(
            dialect,
            modelType,
            "Change",
            id
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(3, "var collection" + id + " = " + field + ";");
        code.AppendLineAt(
            3,
            "if (collection" + id + " is not null && !collection" + id + ".__SparseIsEmpty())"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var entry" + id + " = new " + change + "();");
        code.AppendLineAt(4, "collection" + id + ".__SparseExportChangePayload(entry" + id + ");");
        code.AppendLineAt(4, "changes.Add(entry" + id + ");");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "}");
    }

    /// <summary>Emits the payload export helper inside a collection patch class.</summary>
    /// <remarks>Runs inside the collection patch so whole/item internals stay accessible without widening their visibility.</remarks>
    private static void AppendPatchFromCoreMethod(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType
    )
    {
        var core = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "Core");
        var rootChange = SparseChangeSetPayloadEmitter.PayloadTypeName(
            dialect,
            modelType,
            "RootChange"
        );
        var runtime = dialect.RuntimeNamespace;
        var state = runtime + "ChangePayloadState";
        code.AppendLineAt(
            2,
            "/// <summary>Projects a validated command core to a baseline-free patch.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Before-states are ignored rather than validated; malformed after-states are rejected.</remarks>"
        );
        code.AppendLineAt(2, "/// <param name=\"payload\">The validated command core.</param>");
        code.AppendLineAt(2, "/// <returns>The baseline-free patch.</returns>");
        code.AppendLineAt(2, "internal static Patch PatchFromPayloadCore(" + core + " payload)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (payload is null) throw new global::System.ArgumentNullException(nameof(payload));"
        );
        code.AppendLineAt(
            3,
            "if (payload.Changes is null) throw new global::System.ArgumentException(\"Payload changes must not be null.\", nameof(payload));"
        );
        code.AppendLineAt(3, "var patch = new Patch();");
        code.AppendLineAt(3, "foreach (var change in payload.Changes)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "switch (change)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "case " + rootChange + " whole:");
        code.AppendLineAt(
            6,
            "if (payload.Changes.Count != 1) throw new global::System.ArgumentException(\"A whole-root payload must be the only change.\", nameof(payload));"
        );
        code.AppendLineAt(
            6,
            "if (whole.Before is null || whole.After is null) throw new global::System.ArgumentException(\"A whole-root payload must contain both endpoints.\", nameof(payload));"
        );
        code.AppendLineAt(
            6,
            "if (whole.After.State == "
                + state
                + ".Redacted) throw new global::System.ArgumentException(\"A payload after-state must be observable.\", nameof(payload));"
        );
        code.AppendLineAt(6, "var wholeAfter = __SparsePayloadFragment(whole.After);");
        code.AppendLineAt(
            6,
            "if (!wholeAfter.IsPresent) { var wholePatch = new Patch(); wholePatch.__SparseRemove(); return wholePatch; }"
        );
        code.AppendLineAt(
            6,
            "if (wholeAfter.Value is null) { var wholePatch = new Patch(); wholePatch.__SparseSetNull(); return wholePatch; }"
        );
        code.AppendLineAt(6, "return new Patch(wholeAfter.Value);");
        foreach (var member in members.Where(static member => !member.Property.IsJsonIgnored))
            AppendPatchFromCoreMember(code, member, dialect, modelType, runtime, state);
        code.AppendLineAt(
            5,
            "default: throw new global::System.ArgumentException(\"Payload contains a null or unknown change variant.\", nameof(payload));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return patch;");
        code.AppendLineAt(2, "}");
    }

    private static void AppendPatchFromCoreMember(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType,
        string runtime,
        string state
    )
    {
        var id = member.Id;
        var change = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(
            dialect,
            modelType,
            "Change",
            id
        );
        var esc = SparseNaming.EscapeIdentifier(member.Property.Name);
        if (SparseChangeSetBasicsEmitter.IsNested(member))
        {
            code.AppendLineAt(5, "case " + change + " entry" + id + ":");
            code.AppendLineAt(
                6,
                "if (entry"
                    + id
                    + ".Nested is null) throw new global::System.ArgumentException(\"Nested payload is required.\", nameof(payload));"
            );
            code.AppendLineAt(6, "patch." + esc + " = entry" + id + ".Nested.ToPatchCore();");
            code.AppendLineAt(6, "break;");
        }
        else if (
            SparseChangeSetBasicsEmitter.IsKeyed(member)
            || SparseChangeSetBasicsEmitter.IsDict(member)
        )
        {
            AppendPatchFromCoreCollection(
                code,
                member,
                dialect,
                modelType,
                runtime,
                state,
                id,
                esc
            );
        }
        else
        {
            var valueType = SparseChangeSetBasicsEmitter.FragmentValueType(member);
            var operation = runtime + "FragmentOperation<" + valueType + ">";
            code.AppendLineAt(5, "case " + change + " entry" + id + ":");
            code.AppendLineAt(
                6,
                "if (entry"
                    + id
                    + ".Before is null || entry"
                    + id
                    + ".After is null) throw new global::System.ArgumentException(\"Both transition endpoints are required.\", nameof(payload));"
            );
            code.AppendLineAt(
                6,
                "if (entry"
                    + id
                    + ".After.State == "
                    + state
                    + ".Redacted) throw new global::System.ArgumentException(\"A payload after-state must be observable.\", nameof(payload));"
            );
            code.AppendLineAt(6, "var after" + id + " = entry" + id + ".After.ToOptional();");
            code.AppendLineAt(
                6,
                "patch."
                    + esc
                    + " = after"
                    + id
                    + ".IsPresent ? "
                    + operation
                    + ".Set(after"
                    + id
                    + ".Value) : "
                    + operation
                    + ".Remove;"
            );
            code.AppendLineAt(6, "break;");
        }
    }

    private static void AppendPatchFromCoreCollection(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType,
        string runtime,
        string state,
        int id,
        string esc
    )
    {
        var change = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(
            dialect,
            modelType,
            "Change",
            id
        );
        var collectionPatch =
            "Patch." + SparseFragmentPatchEmitter.GetCollectionPatchName(dialect, member);
        var isKeyed = SparseChangeSetBasicsEmitter.IsKeyed(member);
        code.AppendLineAt(5, "case " + change + " entry" + id + ":");
        code.AppendLineAt(
            6,
            "if (entry"
                + id
                + ".Items is null) throw new global::System.ArgumentException(\"Payload item changes must not be null.\", nameof(payload));"
        );
        code.AppendLineAt(
            6,
            "if (entry" + id + ".Before is not null || entry" + id + ".After is not null)"
        );
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (entry"
                + id
                + ".Before is null || entry"
                + id
                + ".After is null || entry"
                + id
                + ".Items.Count != 0) throw new global::System.ArgumentException(\"A whole collection payload must contain both endpoints and no item changes.\", nameof(payload));"
        );
        code.AppendLineAt(
            7,
            "if (entry"
                + id
                + ".After.State == "
                + state
                + ".Redacted) throw new global::System.ArgumentException(\"A payload after-state must be observable.\", nameof(payload));"
        );
        code.AppendLineAt(7, "var wholeAfter" + id + " = entry" + id + ".After.ToOptional();");
        code.AppendLineAt(7, "var whole" + id + " = new " + collectionPatch + "();");
        code.AppendLineAt(
            7,
            "if (!wholeAfter"
                + id
                + ".IsPresent) whole"
                + id
                + ".Remove(); else whole"
                + id
                + ".Set(wholeAfter"
                + id
                + ".Value!);"
        );
        code.AppendLineAt(7, "patch." + esc + " = whole" + id + ";");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "else");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(7, "var collection" + id + " = new " + collectionPatch + "();");
        if (!isKeyed)
        {
            var itemKind = runtime + "ChangePayloadItemKind";
            code.AppendLineAt(7, "int removeCount" + id + " = 0;");
            code.AppendLineAt(7, "foreach (var reserveItem" + id + " in entry" + id + ".Items)");
            code.AppendLineAt(
                8,
                "if (reserveItem"
                    + id
                    + " is not null && reserveItem"
                    + id
                    + ".Kind == "
                    + itemKind
                    + ".Remove) removeCount"
                    + id
                    + "++;"
            );
            code.AppendLineAt(
                7,
                "if (removeCount"
                    + id
                    + " > 0) collection"
                    + id
                    + ".__SparseReserveRemovals(removeCount"
                    + id
                    + ");"
            );
        }
        if (isKeyed)
            code.AppendLineAt(7, "bool needsOrder" + id + " = false;");
        code.AppendLineAt(7, "foreach (var changeItem" + id + " in entry" + id + ".Items)");
        code.AppendLineAt(7, "{");
        AppendPatchFromCoreItem(code, member, runtime, state, id);
        code.AppendLineAt(7, "}");
        if (isKeyed)
        {
            var keyType = SparseChangeSetBasicsEmitter.KeyTypeOf(member);
            code.AppendLineAt(
                7,
                "if (needsOrder"
                    + id
                    + " && entry"
                    + id
                    + ".AfterOrder is null) throw new global::System.ArgumentException(\"A reordered payload item requires an after-order.\", nameof(payload));"
            );
            code.AppendLineAt(
                7,
                "if (entry"
                    + id
                    + ".AfterOrder is not null && (entry"
                    + id
                    + ".BeforeOrder is null || !"
                    + dialect.RuntimeFacade
                    + ".KeyOrderEquals<"
                    + keyType
                    + ">(entry"
                    + id
                    + ".BeforeOrder, entry"
                    + id
                    + ".AfterOrder))) collection"
                    + id
                    + ".SetOrder(entry"
                    + id
                    + ".AfterOrder);"
            );
        }
        code.AppendLineAt(7, "patch." + esc + " = collection" + id + ";");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "break;");
    }

    private static void AppendPatchFromCoreItem(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string runtime,
        string state,
        int id
    )
    {
        var isKeyed = SparseChangeSetBasicsEmitter.IsKeyed(member);
        var itemKind = runtime + "ChangePayloadItemKind";
        var isModelValue = isKeyed
            ? member.Collection.ElementType.IsFragmentModel
            : member.Collection.ValueType?.IsFragmentModel == true;
        code.AppendLineAt(
            8,
            "if (changeItem"
                + id
                + " is null) throw new global::System.ArgumentException(\"Payload item is required.\", nameof(payload));"
        );
        code.AppendLineAt(
            8,
            "if (changeItem"
                + id
                + ".Kind != "
                + itemKind
                + ".Add && changeItem"
                + id
                + ".Kind != "
                + itemKind
                + ".Remove && changeItem"
                + id
                + ".Kind != "
                + itemKind
                + ".Edit && changeItem"
                + id
                + ".Kind != "
                + itemKind
                + ".Reorder) throw new global::System.ArgumentException(\"Unsupported payload item kind.\", nameof(payload));"
        );
        if (!isKeyed)
            code.AppendLineAt(
                8,
                "if (changeItem"
                    + id
                    + ".Kind == "
                    + itemKind
                    + ".Reorder) throw new global::System.ArgumentException(\"Reorder is not supported for dictionary payload items.\", nameof(payload));"
            );
        code.AppendLineAt(8, "if (changeItem" + id + ".Kind == " + itemKind + ".Add)");
        code.AppendLineAt(8, "{");
        code.AppendLineAt(
            9,
            "if (changeItem"
                + id
                + ".Before is not null) throw new global::System.ArgumentException(\"An added payload item must omit 'before'.\", nameof(payload));"
        );
        code.AppendLineAt(
            9,
            "if (changeItem"
                + id
                + ".After is null || changeItem"
                + id
                + ".After.State == "
                + state
                + ".Redacted) throw new global::System.ArgumentException(\"An added payload item must contain an observable 'after'.\", nameof(payload));"
        );
        code.AppendLineAt(9, "var added" + id + " = changeItem" + id + ".After.ToOptional();");
        code.AppendLineAt(
            9,
            "if (!added"
                + id
                + ".IsPresent) throw new global::System.ArgumentException(\"An added payload item must contain 'after'.\", nameof(payload));"
        );
        if (isKeyed)
            code.AppendLineAt(9, "collection" + id + ".Add(added" + id + ".Value!);");
        else
            code.AppendLineAt(
                9,
                "collection" + id + ".SetEntry(changeItem" + id + ".Key, added" + id + ".Value!);"
            );
        code.AppendLineAt(8, "}");
        code.AppendLineAt(8, "else if (changeItem" + id + ".Kind == " + itemKind + ".Remove)");
        code.AppendLineAt(8, "{");
        code.AppendLineAt(
            9,
            "if (changeItem"
                + id
                + ".After is not null) throw new global::System.ArgumentException(\"A removed payload item must omit 'after'.\", nameof(payload));"
        );
        if (isKeyed)
            code.AppendLineAt(9, "collection" + id + ".Remove(changeItem" + id + ".Key);");
        else
            code.AppendLineAt(9, "collection" + id + ".RemoveEntry(changeItem" + id + ".Key);");
        code.AppendLineAt(8, "}");
        code.AppendLineAt(8, "else if (changeItem" + id + ".Kind == " + itemKind + ".Edit)");
        code.AppendLineAt(8, "{");
        if (isModelValue)
        {
            code.AppendLineAt(
                9,
                "if (changeItem"
                    + id
                    + ".Before is not null || changeItem"
                    + id
                    + ".After is not null) throw new global::System.ArgumentException(\"An edited model payload item must omit 'before' and 'after'.\", nameof(payload));"
            );
            code.AppendLineAt(
                9,
                "if (changeItem"
                    + id
                    + ".Edit is null) throw new global::System.ArgumentException(\"Edited payload items require an edit payload.\", nameof(payload));"
            );
            code.AppendLineAt(
                9,
                "collection"
                    + id
                    + ".__SparseSetEdited(changeItem"
                    + id
                    + ".Key, changeItem"
                    + id
                    + ".Edit.ToPatchCore());"
            );
        }
        else
        {
            code.AppendLineAt(
                9,
                "if (changeItem"
                    + id
                    + ".After is null || changeItem"
                    + id
                    + ".After.State == "
                    + state
                    + ".Redacted) throw new global::System.ArgumentException(\"An edited scalar payload item must contain an observable 'after'.\", nameof(payload));"
            );
            code.AppendLineAt(9, "var edited" + id + " = changeItem" + id + ".After.ToOptional();");
            code.AppendLineAt(
                9,
                "if (!edited"
                    + id
                    + ".IsPresent) throw new global::System.ArgumentException(\"An edited scalar payload item must contain 'after'.\", nameof(payload));"
            );
            if (isKeyed)
                code.AppendLineAt(9, "collection" + id + ".Update(edited" + id + ".Value!);");
            else
                code.AppendLineAt(
                    9,
                    "collection"
                        + id
                        + ".UpdateEntry(changeItem"
                        + id
                        + ".Key, edited"
                        + id
                        + ".Value!);"
                );
        }
        code.AppendLineAt(8, "}");
        code.AppendLineAt(8, "else");
        code.AppendLineAt(8, "{");
        if (isKeyed)
        {
            code.AppendLineAt(
                9,
                "if (changeItem"
                    + id
                    + ".Before is not null || changeItem"
                    + id
                    + ".After is not null) throw new global::System.ArgumentException(\"A reordered payload item must omit 'before' and 'after'.\", nameof(payload));"
            );
            code.AppendLineAt(
                9,
                "if (changeItem"
                    + id
                    + ".Edit is not null) throw new global::System.ArgumentException(\"A reordered payload item must not contain 'edit'.\", nameof(payload));"
            );
            code.AppendLineAt(9, "needsOrder" + id + " = true;");
        }
        code.AppendLineAt(8, "}");
    }
}
