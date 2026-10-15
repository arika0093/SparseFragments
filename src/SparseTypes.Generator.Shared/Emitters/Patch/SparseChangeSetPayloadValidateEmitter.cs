using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits the serializer-independent structural payload validation.</summary>
/// <remarks>Validation mirrors <c>__SparseMixedPartition</c> acceptance without
/// throwing: blind and redacted envelopes are valid, and every routine structural
/// problem becomes a typed error. Conversion and rebase conflicts stay separate.</remarks>
internal static class SparseChangeSetPayloadValidateEmitter
{
    internal static void AppendCoreValidate(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType
    )
    {
        if (modelType is null)
            return;
        var runtime = dialect.RuntimeNamespace;
        var result = runtime + "ChangePayloadValidationResult";
        var error = runtime + "ChangePayloadValidationError";
        var kind = runtime + "ChangePayloadValidationKind";
        var errors = "global::System.Collections.Generic.List<" + error + ">";
        code.AppendLineAt(
            2,
            "/// <summary>Validates the structural wire contract without converting or applying.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Blind and redacted envelopes are valid; a valid payload can still be unconvertible to a complete <c>ChangeSet</c>, and rebase conflicts are reported by <c>TryApplyMixedTo</c>, not here. Messages carry paths and reasons only, never secret values.</remarks>"
        );
        code.AppendLineAt(
            2,
            "/// <returns>The validation outcome in deterministic payload order.</returns>"
        );
        code.AppendLineAt(2, "public " + result + " Validate()");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var __validateErrors = new " + errors + "();");
        code.AppendLineAt(3, "__SparseValidateInto(__validateErrors, string.Empty);");
        code.AppendLineAt(3, "return new " + result + "(__validateErrors);");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Collects structural errors with the given path prefix.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Accepts the same envelopes as <c>__SparseMixedPartition</c>; nested cores and item edits recurse with extended prefixes.</remarks>"
        );
        code.AppendLineAt(2, "/// <param name=\"errors\">The collected errors.</param>");
        code.AppendLineAt(2, "/// <param name=\"pathPrefix\">The dotted path prefix.</param>");
        code.AppendLineAt(
            2,
            "internal void __SparseValidateInto(" + errors + " errors, string pathPrefix)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (errors is null) throw new global::System.ArgumentNullException(nameof(errors));"
        );
        code.AppendLineAt(
            3,
            "if (pathPrefix is null) throw new global::System.ArgumentNullException(nameof(pathPrefix));"
        );
        code.AppendLineAt(
            3,
            "if (Changes is null) { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Changes, Path = pathPrefix, Message = \"Payload changes must not be null.\" }); return; }"
        );
        AppendWholeValidate(code, members, dialect, modelType, runtime, error, kind);
        foreach (var member in members.Where(static member => !member.Property.IsJsonIgnored))
        {
            code.AppendLineAt(3, "bool __validateSeen" + member.Id + " = false;");
        }
        code.AppendLineAt(3, "foreach (var change in Changes)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "switch (change)");
        code.AppendLineAt(4, "{");
        foreach (var member in members.Where(static member => !member.Property.IsJsonIgnored))
        {
            AppendMemberValidate(code, member, dialect, modelType, runtime, error, kind);
        }
        code.AppendLineAt(
            5,
            "default: errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Changes, Path = pathPrefix, Message = \"Payload contains a null or unknown change variant.\" }); break;"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
    }

    internal static void AppendEnvelopeValidate(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var result = runtime + "ChangePayloadValidationResult";
        var error = runtime + "ChangePayloadValidationError";
        var kind = runtime + "ChangePayloadValidationKind";
        var versionLiteral = SymbolDisplay.FormatLiteral(dialect.ChangePayloadVersion, true);
        code.AppendLineAt(
            2,
            "/// <summary>Validates the envelope wire contract without converting or applying.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Validates the envelope wire version before interpreting the payload.</remarks>"
        );
        code.AppendLineAt(
            2,
            "/// <returns>The validation outcome in deterministic payload order.</returns>"
        );
        code.AppendLineAt(2, "public new " + result + " Validate()");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (!global::System.String.Equals(Version, "
                + versionLiteral
                + ", global::System.StringComparison.Ordinal)) return new "
                + result
                + "(new "
                + error
                + "[] { new "
                + error
                + " { Kind = "
                + kind
                + ".Version, Path = string.Empty, Message = \"Unsupported ChangePayload version.\" } });"
        );
        code.AppendLineAt(3, "return base.Validate();");
        code.AppendLineAt(2, "}");
    }

    private static void AppendWholeValidate(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        string runtime,
        string error,
        string kind
    )
    {
        _ = members;
        _ = dialect;
        _ = runtime;
        var rootChange = SparseChangeSetPayloadEmitter.PayloadName(modelType, "RootChange");
        var rootPath = "pathPrefix + \"$root\"";
        code.AppendLineAt(3, "if (Changes.Count == 1 && Changes[0] is " + rootChange + " whole)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (whole.Before is null || whole.After is null) { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Changes, Path = "
                + rootPath
                + ", Message = \"A whole-root payload must contain both endpoints.\" }); return; }"
        );
        // Value-free endpoints must not smuggle values; reuse the endpoint
        // check so validation and conversion cannot disagree.
        AppendEndpointValidate(
            code,
            4,
            "whole.Before",
            rootPath,
            runtime,
            error,
            kind,
            "errors",
            "__validateWholeBeforeBad"
        );
        AppendEndpointValidate(
            code,
            4,
            "whole.After",
            rootPath,
            runtime,
            error,
            kind,
            "errors",
            "__validateWholeAfterBad"
        );
        code.AppendLineAt(4, "if (__validateWholeBeforeBad || __validateWholeAfterBad) return;");
        code.AppendLineAt(
            4,
            "if (whole.After.IsRedacted) { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Endpoint, Path = "
                + rootPath
                + ", Message = \"The payload contains a redacted after-state for '\" + "
                + rootPath
                + " + \"'. After-states must stay concrete (value, null, or missing).\" }); return; }"
        );
        code.AppendLineAt(4, "if (whole.Before.IsRedacted)");
        code.AppendLineAt(4, "{");
        // A blind whole-root set is valid; only its conversion shape is
        // checked, reusing the endpoint conversion.
        code.AppendLineAt(4, "try { whole.After.ToOptional(); }");
        code.AppendLineAt(
            4,
            "catch (global::System.InvalidOperationException __validateWholeBlindEx) { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Endpoint, Path = "
                + rootPath
                + ", Message = __validateWholeBlindEx.Message }); }"
        );
        code.AppendLineAt(4, "return;");
        code.AppendLineAt(4, "}");
        // An ordinary whole-root snapshot converts through the same fragment
        // reader as the partition, reusing its member checks.
        code.AppendLineAt(4, "try");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "var __validateWholeBefore = whole.Before.ToOptional();");
        code.AppendLineAt(5, "var __validateWholeAfter = whole.After.ToOptional();");
        code.AppendLineAt(
            5,
            "if (__validateWholeBefore.IsPresent) __validateWholeBefore.Value?.ToFragment();"
        );
        code.AppendLineAt(
            5,
            "if (__validateWholeAfter.IsPresent) __validateWholeAfter.Value?.ToFragment();"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "catch (global::System.ArgumentException __validateWholeEx) { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Changes, Path = "
                + rootPath
                + ", Message = __validateWholeEx.Message }); }"
        );
        code.AppendLineAt(
            4,
            "catch (global::System.InvalidOperationException __validateWholeEx) { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Endpoint, Path = "
                + rootPath
                + ", Message = __validateWholeEx.Message }); }"
        );
        code.AppendLineAt(4, "return;");
        code.AppendLineAt(3, "}");
    }

    private static void AppendMemberValidate(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        string runtime,
        string error,
        string kind
    )
    {
        var id = member.Id;
        var propLit = SymbolDisplay.FormatLiteral(member.Property.Name, true);
        var path = "pathPrefix + " + propLit;
        var variant = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(
            dialect,
            modelType,
            "Change",
            id
        );
        code.AppendLineAt(5, "case " + variant + " item:");
        code.AppendLineAt(
            6,
            "if (__validateSeen"
                + id
                + ") { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Duplicate, Path = "
                + path
                + ", Message = \"A payload cannot contain duplicate member changes.\" }); break; }"
        );
        code.AppendLineAt(6, "__validateSeen" + id + " = true;");
        if (SparseChangeSetBasicsEmitter.IsNested(member))
        {
            code.AppendLineAt(
                6,
                "if (item.Nested is null) { errors.Add(new "
                    + error
                    + " { Kind = "
                    + kind
                    + ".Changes, Path = "
                    + path
                    + ", Message = \"Nested payload is required.\" }); break; }"
            );
            code.AppendLineAt(6, "item.Nested.__SparseValidateInto(errors, " + path + " + \".\");");
            code.AppendLineAt(6, "break;");
            return;
        }
        if (
            SparseChangeSetBasicsEmitter.IsKeyed(member)
            || SparseChangeSetBasicsEmitter.IsDict(member)
        )
        {
            SparseChangeSetPayloadValidateKeyedEmitter.AppendKeyedValidate(
                code,
                member,
                dialect,
                modelType,
                path,
                runtime,
                error,
                kind
            );
            return;
        }
        if (
            SparseDownstreamPolicy.IsScalarMember(member)
            && dialect.GetTransport(member.Property.Name) != SparseMemberTransport.Full
        )
        {
            // Transport policy mirrors the restore seam: an undisclosed
            // before-state can never rebuild a complete ChangeSet.
            code.AppendLineAt(
                6,
                "if (item.Before is not null) { errors.Add(new "
                    + error
                    + " { Kind = "
                    + kind
                    + ".Endpoint, Path = "
                    + path
                    + ", Message = \"A redacted member must omit its before-state.\" }); break; }"
            );
            code.AppendLineAt(
                6,
                "if (item.After is null) { errors.Add(new "
                    + error
                    + " { Kind = "
                    + kind
                    + ".Endpoint, Path = "
                    + path
                    + ", Message = \"A redacted member requires an after-state.\" }); break; }"
            );
            code.AppendLineAt(
                6,
                "errors.Add(new "
                    + error
                    + " { Kind = "
                    + kind
                    + ".Endpoint, Path = "
                    + path
                    + ", Message = \"Member '"
                    + member.Property.Name
                    + "' is redacted and cannot be converted to a complete ChangeSet. Use ToPatch() for the baseline-free projection.\" });"
            );
            code.AppendLineAt(6, "break;");
            return;
        }
        AppendScalarValidate(code, member, path, runtime, error, kind);
    }

    private static void AppendScalarValidate(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string path,
        string runtime,
        string error,
        string kind
    )
    {
        var id = member.Id;
        code.AppendLineAt(
            6,
            "if (item.Before is null || item.After is null) { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Endpoint, Path = "
                + path
                + ", Message = \"Both transition endpoints are required.\" }); break; }"
        );
        AppendEndpointValidate(
            code,
            6,
            "item.Before",
            path,
            runtime,
            error,
            kind,
            "errors",
            "__validateBeforeBad" + id
        );
        AppendEndpointValidate(
            code,
            6,
            "item.After",
            path,
            runtime,
            error,
            kind,
            "errors",
            "__validateAfterBad" + id
        );
        code.AppendLineAt(
            6,
            "if (__validateBeforeBad" + id + " || __validateAfterBad" + id + ") break;"
        );
        code.AppendLineAt(
            6,
            "if (item.After.IsRedacted) { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Endpoint, Path = "
                + path
                + ", Message = \"The payload contains a redacted after-state for '\" + "
                + path
                + " + \"'. After-states must stay concrete (value, null, or missing).\" }); break; }"
        );
        code.AppendLineAt(6, "if (item.Before.IsRedacted)");
        code.AppendLineAt(6, "{");
        // A blind scalar set is valid; only its conversion shape is checked.
        code.AppendLineAt(6, "try { item.After.ToOptional(); }");
        code.AppendLineAt(
            6,
            "catch (global::System.InvalidOperationException __validateBlindEx"
                + id
                + ") { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Endpoint, Path = "
                + path
                + ", Message = __validateBlindEx"
                + id
                + ".Message }); }"
        );
        code.AppendLineAt(6, "break;");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "try { item.Before.ToOptional(); item.After.ToOptional(); }");
        code.AppendLineAt(
            6,
            "catch (global::System.InvalidOperationException __validateOrdinaryEx"
                + id
                + ") { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Endpoint, Path = "
                + path
                + ", Message = __validateOrdinaryEx"
                + id
                + ".Message }); }"
        );
        code.AppendLineAt(6, "break;");
    }

    internal static void AppendEndpointValidate(
        SharedIndentedBuilder code,
        int indent,
        string target,
        string path,
        string runtime,
        string error,
        string kind,
        string errorsVar,
        string badFlag
    )
    {
        _ = runtime;
        code.AppendLineAt(indent, "bool " + badFlag + " = false;");
        code.AppendLineAt(indent, "try { " + target + ".Validate(); }");
        code.AppendLineAt(
            indent,
            "catch (global::System.InvalidOperationException "
                + badFlag
                + "Ex) { "
                + errorsVar
                + ".Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Endpoint, Path = "
                + path
                + ", Message = "
                + badFlag
                + "Ex.Message }); "
                + badFlag
                + " = true; }"
        );
    }
}
