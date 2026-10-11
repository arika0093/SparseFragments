using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp;
using static SparseFragments.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits the typed, serializer-facing payload DTOs for one model.</summary>
internal static class SparseChangeSetPayloadEmitter
{
    internal static void AppendPayload(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        ImmutableArray<string> ignoredSettablePropertyNames = default,
        bool emitContainers = true,
        bool emitFacade = true,
        string? implementationNamespace = null,
        string containerAccessibility = "public"
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var endpoint = runtime + "ChangePayloadEndpoint";
        var payloadCore = PayloadName(modelType, "Core");
        var payloadRoot = PayloadName(modelType, "Root");
        var payloadChange = PayloadName(modelType, "Change");
        var variants = members.Where(static member => !member.Property.IsJsonIgnored).ToArray();
        // Write-only members never appear in the read projection; their
        // transport variants below still carry the command after-state.
        var readable = variants
            .Where(member =>
                dialect.GetTransport(member.Property.Name) != SparseMemberTransport.WriteOnly
            )
            .ToArray();

        if (emitContainers)
        {
            AppendPayloadCoreContainer(
                code,
                members,
                dialect,
                modelType,
                ignoredSettablePropertyNames,
                payloadCore,
                payloadChange,
                containerAccessibility
            );
        }

        if (emitFacade)
        {
            AppendChangePayloadFacadeBlock(code, members, dialect, modelType);
        }

        if (emitContainers)
        {
            AppendPayloadVariantContainers(
                code,
                dialect,
                modelType,
                runtime,
                endpoint,
                payloadRoot,
                payloadChange,
                variants,
                readable,
                implementationNamespace,
                containerAccessibility
            );
        }
    }

    private static void AppendPayloadCoreContainer(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        ImmutableArray<string> ignoredSettablePropertyNames,
        string payloadCore,
        string payloadChange,
        string containerAccessibility
    )
    {
        code.AppendLineAt(
            1,
            "/// <summary>Serializer-facing payload DTOs for this model.</summary>"
        );
        code.AppendLineAt(
            1,
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
        );
        code.AppendLineAt(
            1,
            containerAccessibility
                + " static partial class "
                + RequirePayloadContainerName(dialect, modelType)
        );
        code.AppendLineAt(1, "{");
        code.IndentOffset++;
        code.AppendLineAt(
            1,
            "/// <summary>Transport core carrying the validated member changes.</summary>"
        );
        code.AppendLineAt(
            1,
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
        );
        code.AppendLineAt(
            1,
            "[global::System.Text.Json.Serialization.JsonUnmappedMemberHandling(global::System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]"
        );
        code.AppendLineAt(1, "public class " + payloadCore);
        code.AppendLineAt(1, "{");
        // XML docs precede serialization attributes so the compiler associates
        // them with the member (CS1591).
        code.AppendLineAt(
            2,
            "/// <summary>Gets or sets the member changes carried by this core.</summary>"
        );
        AppendJsonProperty(code, 2, "Changes", 1);
        code.AppendLineAt(
            2,
            "public global::System.Collections.Generic.List<"
                + payloadChange
                + ">? Changes { get; set; }"
        );
        // Public DTO surface precedes the internal conversion seams so the
        // emitted container reads public -> internal -> private.
        SparseChangeSetMixedEmitter.AppendMixedPayloadSurface(
            code,
            members,
            dialect,
            modelType,
            ignoredSettablePropertyNames
        );
        if (modelType is not null)
            code.AppendLineAt(
                2,
                "internal "
                    + modelType
                    + ".ChangeSet ToChangeSetCore() => "
                    + modelType
                    + ".ChangeSet.FromPayloadCore(this);"
            );
        if (modelType is not null && !SparseDownstreamPolicy.HasAnyNonFullPolicy(dialect))
            code.AppendLineAt(
                2,
                "internal "
                    + modelType
                    + ".Patch ToPatchCore() => "
                    + modelType
                    + ".ChangeSet.PatchFromPayloadCore(this);"
            );
        if (modelType is not null)
            code.AppendLineAt(
                2,
                "internal static "
                    + payloadCore
                    + " FromPatchCore("
                    + modelType
                    + ".Patch patch) => patch.ToChangePayloadCore();"
            );
        if (modelType is not null && SparseDownstreamPolicy.HasAnyNonFullPolicy(dialect))
        {
            SparseChangeSetPayloadProjectionEmitter.AppendCoreToPatch(
                code,
                members,
                dialect,
                modelType
            );
        }
        code.AppendLineAt(1, "}");
        code.IndentOffset--;
        code.AppendLineAt(1, "}");
        code.AppendLine();
    }

    private static void AppendChangePayloadFacadeBlock(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType
    )
    {
        code.AppendLineAt(
            1,
            "/// <summary>Serializable envelope carrying the validated transition for transport.</summary>"
        );
        code.AppendLineAt(
            1,
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Advanced)]"
        );
        code.AppendLineAt(
            1,
            "[global::System.Text.Json.Serialization.JsonUnmappedMemberHandling(global::System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]"
        );
        code.AppendLineAt(
            1,
            "public sealed class ChangePayload : " + PayloadTypeName(dialect, modelType, "Core")
        );
        code.AppendLineAt(1, "{");
        code.AppendLineAt(
            2,
            "/// <summary>Gets or sets the provisional wire version token.</summary>"
        );
        AppendJsonProperty(code, 2, "Version", 0);
        code.AppendLineAt(2, "public string? Version { get; set; }");
        if (modelType is not null)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Converts this validated envelope to a complete change set.</summary>"
            );
            code.AppendLineAt(
                2,
                "/// <remarks>Redacted or otherwise incomplete histories are rejected; project them with <see cref=\"ToPatch\"/> instead.</remarks>"
            );
            code.AppendLineAt(2, "/// <returns>The validated change set.</returns>");
            code.AppendLineAt(
                2,
                "public "
                    + modelType
                    + ".ChangeSet ToChangeSet() => "
                    + modelType
                    + ".ChangeSet.FromPayload(this);"
            );
            // Baseline-discarding projection is owned by the payload core (mixed
            // partition) so ordinary transitions and redacted-before blind sets
            // route identically; the envelope re-declares it to keep the
            // conversion discoverable on the validated envelope type. When member
            // transport policies apply the downstream projection owns ToPatch
            // instead (strict-aware), so the mixed re-declaration is skipped to
            // keep a single seam with no duplicate member.
            if (!SparseDownstreamPolicy.HasOwnNonFullPolicy(members, dialect))
            {
                code.AppendLineAt(
                    2,
                    "/// <summary>Discards baseline information and returns the equivalent desired-operation patch.</summary>"
                );
                code.AppendLineAt(
                    2,
                    "/// <remarks>Redacted before-states project to their requested after-state without historical comparison; ordinary members project their after-state too. The result is baseline-free and can no longer rebase or report conflicts.</remarks>"
                );
                code.AppendLineAt(2, "/// <returns>The baseline-free patch.</returns>");
                code.AppendLineAt(2, "public new " + modelType + ".Patch ToPatch()");
                code.AppendLineAt(2, "{");
                SparseChangeSetMixedEmitter.AppendVersionGuard(code, dialect);
                code.AppendLineAt(3, "return base.ToPatch();");
                code.AppendLineAt(2, "}");
            }
            // The wire version lives on this envelope: every public
            // interpretation path validates it before reaching the
            // versionless core seam (issue #163).
            SparseChangeSetMixedEmitter.AppendEnvelopeVersionOverrides(code, dialect, modelType);
            code.AppendLineAt(
                2,
                "/// <summary>Builds a baseline-free command envelope from a patch.</summary>"
            );
            code.AppendLineAt(
                2,
                "/// <remarks>Before-states are redacted by construction; the result only supports <see cref=\"ToPatch\"/>.</remarks>"
            );
            code.AppendLineAt(2, "/// <param name=\"patch\">The patch to convert.</param>");
            code.AppendLineAt(2, "/// <returns>The baseline-free command envelope.</returns>");
            code.AppendLineAt(
                2,
                "public static ChangePayload FromPatch(" + modelType + ".Patch patch)"
            );
            code.AppendLineAt(2, "{");
            code.AppendLineAt(
                3,
                "if (patch is null) throw new global::System.ArgumentNullException(nameof(patch));"
            );
            code.AppendLineAt(
                3,
                "return new ChangePayload { Version = "
                    + SymbolDisplay.FormatLiteral(dialect.ChangePayloadVersion, true)
                    + ", Changes = "
                    + PayloadTypeName(dialect, modelType, "Core")
                    + ".FromPatchCore(patch).Changes };"
            );
            code.AppendLineAt(2, "}");
            SparseChangeSetPayloadProjectionEmitter.AppendRootToPatch(
                code,
                members,
                dialect,
                modelType
            );
        }
        code.AppendLineAt(1, "}");
        code.AppendLine();
    }

    private static void AppendPayloadVariantContainers(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        string runtime,
        string endpoint,
        string payloadRoot,
        string payloadChange,
        SparseMemberModel[] variants,
        SparseMemberModel[] readable,
        string? implementationNamespace,
        string containerAccessibility
    )
    {
        code.AppendLineAt(
            1,
            containerAccessibility
                + " static partial class "
                + RequirePayloadContainerName(dialect, modelType)
        );
        code.AppendLineAt(1, "{");
        code.IndentOffset++;
        code.AppendLineAt(
            1,
            "/// <summary>Snapshot of the present member values for whole-root transport.</summary>"
        );
        code.AppendLineAt(
            1,
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
        );
        code.AppendLineAt(1, "public sealed class " + payloadRoot);
        code.AppendLineAt(1, "{");
        code.AppendLineAt(2, "/// <summary>Gets or sets the snapshot member values.</summary>");
        AppendJsonProperty(code, 2, "Members", 0);
        code.AppendLineAt(
            2,
            "public global::System.Collections.Generic.List<"
                + payloadChange
                + "> Members { get; set; } = new();"
        );
        code.AppendLineAt(2, "/// <summary>Builds a snapshot from a fragment.</summary>");
        code.AppendLineAt(2, "/// <param name=\"value\">The fragment to snapshot.</param>");
        code.AppendLineAt(2, "/// <returns>The snapshot root.</returns>");
        code.AppendLineAt(2, "public static " + payloadRoot + " FromFragment(Fragment value)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var result = new " + payloadRoot + "();");
        SparseChangeSetPayloadSnapshotEmitter.AppendFromFragmentMembers(
            code,
            System.Collections.Immutable.ImmutableArray.CreateRange(readable),
            endpoint,
            runtime,
            dialect,
            modelType,
            false,
            implementationNamespace
        );
        code.AppendLineAt(3, "return result;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(2, "/// <summary>Converts this snapshot to a fragment.</summary>");
        code.AppendLineAt(2, "/// <returns>The converted fragment.</returns>");
        code.AppendLineAt(2, "public Fragment ToFragment()");
        // The redacting overload stays internal and follows the public
        // surface so the emitted container reads public -> internal.
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (Members is null) throw new global::System.ArgumentException(\"Root members must not be null.\", nameof(Members));"
        );
        foreach (var member in readable)
        {
            var valueType = SparseChangeSetBasicsEmitter.IsNested(member)
                ? member.ChildFragmentType + "?"
                : SparseChangeSetBasicsEmitter.FragmentValueType(member);
            code.AppendLineAt(
                3,
                runtime
                    + "Optional<"
                    + valueType
                    + "> rootMember"
                    + member.Id
                    + " = "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Missing;"
            );
        }
        foreach (var member in readable)
            code.AppendLineAt(3, "bool seen" + member.Id + " = false;");
        code.AppendLineAt(3, "foreach (var member in Members)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "switch (member)");
        code.AppendLineAt(4, "{");
        foreach (var member in readable)
        {
            code.AppendLineAt(
                5,
                "case " + PayloadMemberName(modelType, "Change", member.Id) + " item:"
            );
            code.AppendLineAt(
                6,
                "if (seen"
                    + member.Id
                    + ") throw new global::System.ArgumentException(\"Root contains duplicate member values.\", nameof(Members));"
            );
            code.AppendLineAt(6, "seen" + member.Id + " = true;");
            code.AppendLineAt(
                6,
                "if (item.Value is null) throw new global::System.ArgumentException(\"Root member value is required.\", nameof(Members));"
            );
            code.AppendLineAt(
                6,
                "if (item.Value.State == "
                    + runtime
                    + "ChangePayloadState.Redacted) throw new global::System.ArgumentException(\"A redacted root member cannot convert to a fragment.\", nameof(Members));"
            );
            if (SparseChangeSetBasicsEmitter.IsNested(member))
            {
                var childFragment = member.ChildFragmentType + "?";
                code.AppendLineAt(6, "var member" + member.Id + " = item.Value.ToOptional();");
                code.AppendLineAt(
                    6,
                    "rootMember"
                        + member.Id
                        + " = member"
                        + member.Id
                        + ".IsPresent ? "
                        + runtime
                        + "Optional<"
                        + childFragment
                        + ">.Present(member"
                        + member.Id
                        + ".Value is null ? null : member"
                        + member.Id
                        + ".Value.ToFragment()) : "
                        + runtime
                        + "Optional<"
                        + childFragment
                        + ">.Missing;"
                );
            }
            else
            {
                code.AppendLineAt(6, "rootMember" + member.Id + " = item.Value.ToOptional();");
            }
            code.AppendLineAt(6, "break;");
        }
        code.AppendLineAt(
            5,
            "default: throw new global::System.ArgumentException(\"Root contains an unknown member value.\", nameof(Members));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return new Fragment");
        code.AppendLineAt(3, "{");
        foreach (var member in readable)
            code.AppendLineAt(
                4,
                SparseNaming.EscapeIdentifier(member.Property.Name)
                    + " = rootMember"
                    + member.Id
                    + ","
            );
        code.AppendLineAt(3, "};");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "internal static " + payloadRoot + " FromFragment(Fragment value, bool redactBefores)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var result = new " + payloadRoot + "();");
        SparseChangeSetPayloadSnapshotEmitter.AppendFromFragmentMembers(
            code,
            System.Collections.Immutable.ImmutableArray.CreateRange(readable),
            endpoint,
            runtime,
            dialect,
            modelType,
            true,
            implementationNamespace
        );
        code.AppendLineAt(3, "return result;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(1, "}");
        code.AppendLine();

        // XML docs precede serialization attributes so the compiler associates
        // them with the member (CS1591).
        code.AppendLineAt(
            1,
            "/// <summary>Base type for the per-member change variants.</summary>"
        );
        code.AppendLineAt(
            1,
            "[global::System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = \"member\")]"
        );
        var rootDiscriminatorBuilder = new System.Text.StringBuilder("$root");
        while (variants.Any(member => member.Property.Name == rootDiscriminatorBuilder.ToString()))
            rootDiscriminatorBuilder.Append('$');
        var rootDiscriminator = rootDiscriminatorBuilder.ToString();
        code.AppendLineAt(
            1,
            "[global::System.Text.Json.Serialization.JsonDerivedType(typeof("
                + PayloadName(modelType, "RootChange")
                + "), "
                + SymbolDisplay.FormatLiteral(rootDiscriminator, true)
                + ")]"
        );
        foreach (var member in variants)
        {
            code.AppendLineAt(
                1,
                "[global::System.Text.Json.Serialization.JsonDerivedType(typeof("
                    + PayloadMemberName(modelType, "Change", member.Id)
                    + "), "
                    + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                    + ")]"
            );
        }
        code.AppendLineAt(
            1,
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
        );
        code.AppendLineAt(1, "public abstract class " + payloadChange + " { }");
        code.AppendLine();

        code.AppendLineAt(
            1,
            "/// <summary>Whole-root change carrying the before and after snapshots.</summary>"
        );
        code.AppendLineAt(
            1,
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
        );
        code.AppendLineAt(
            1,
            "public sealed class " + PayloadName(modelType, "RootChange") + " : " + payloadChange
        );
        code.AppendLineAt(1, "{");
        code.AppendLineAt(2, "/// <summary>Gets or sets the before snapshot.</summary>");
        AppendIgnoreNull(code, 2);
        AppendJsonProperty(code, 2, "Before", 0);
        code.AppendLineAt(2, "public " + endpoint + "<" + payloadRoot + ">? Before { get; set; }");
        code.AppendLineAt(2, "/// <summary>Gets or sets the after snapshot.</summary>");
        AppendIgnoreNull(code, 2);
        AppendJsonProperty(code, 2, "After", 1);
        code.AppendLineAt(2, "public " + endpoint + "<" + payloadRoot + ">? After { get; set; }");
        code.AppendLineAt(1, "}");
        code.AppendLine();

        foreach (var member in variants)
        {
            SparseChangeSetPayloadItemEmitter.AppendMemberPayload(
                code,
                member,
                endpoint,
                dialect,
                modelType,
                implementationNamespace
            );
            code.AppendLine();
        }
        code.IndentOffset--;
        code.AppendLineAt(1, "}");
    }

    internal static void AppendToPayload(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        SparseOperationTarget? target = null
    ) =>
        // Reloc-2 split: transfer bodies live in the dedicated TransferEmitter;
        // this facade preserves the pre-split call site while reloc-3 routes
        // through the same target-aware implementation.
        SparseChangeSetPayloadTransferEmitter.AppendToPayload(
            code,
            members,
            dialect,
            modelType,
            target
        );

    internal static string PayloadName(string? modelType, string suffix)
    {
        var modelIdentity = modelType ?? "SparseGeneratedModel";
        var shortModelId = SparseNaming.GetStableTypeHash(modelIdentity, CancellationToken.None);
        return suffix + "_" + shortModelId;
    }

    internal static string PayloadMemberName(string? modelType, string suffix, int id)
    {
        var modelIdentity = modelType ?? "SparseGeneratedModel";
        var shortModelId = SparseNaming.GetStableTypeHash(modelIdentity, CancellationToken.None);
        return suffix + id + "_" + shortModelId;
    }

    internal static string PayloadTypeName(
        string payloadContainerName,
        string? modelType,
        string suffix
    ) => payloadContainerName + "." + PayloadName(modelType, suffix);

    internal static string PayloadTypeName(
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        string suffix
    ) => PayloadTypeName(RequirePayloadContainerName(dialect, modelType), modelType, suffix);

    internal static string PayloadMemberTypeName(
        string payloadContainerName,
        string? modelType,
        string suffix,
        int id
    ) => payloadContainerName + "." + PayloadMemberName(modelType, suffix, id);

    internal static string PayloadMemberTypeName(
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        string suffix,
        int id
    ) =>
        PayloadMemberTypeName(
            RequirePayloadContainerName(dialect, modelType),
            modelType,
            suffix,
            id
        );

    internal static string RequirePayloadContainerName(
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType
    ) =>
        !string.IsNullOrWhiteSpace(dialect.PayloadImplementationContainerPrefix)
            ? dialect.PayloadImplementationContainerPrefix
                + "_"
                + SparseNaming.GetStableTypeHash(
                    modelType ?? "SparseGeneratedModel",
                    CancellationToken.None
                )
            : throw new global::System.InvalidOperationException(
                "PayloadImplementationContainerPrefix must be configured when emitting ChangePayload DTOs."
            );

    /// <summary>Resolves the child payload reference for split emission.</summary>
    /// <remarks>Single-file emission keeps the legacy <c>ChildModel.Container.Payload</c>
    /// nesting; split emission qualifies the same container under the
    /// implementation namespace so wire discriminators stay unchanged.</remarks>
    /// <param name="dialect">Owning patch dialect.</param>
    /// <param name="childModelType">Child model type name.</param>
    /// <param name="suffix">Payload suffix.</param>
    /// <param name="implementationNamespace">Explicit namespace, or null for single-file.</param>
    /// <returns>The reference to use from generated code.</returns>
    internal static string QualifiedChildPayloadType(
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string childModelType,
        string suffix,
        string? implementationNamespace
    )
    {
        if (string.IsNullOrEmpty(implementationNamespace))
            return childModelType + "." + PayloadTypeName(dialect, childModelType, suffix);
        var simple = RequirePayloadContainerName(dialect, childModelType);
        return SparseGeneratedPlacement.QualifyPayloadContainer(simple, implementationNamespace)
            + "."
            + PayloadName(childModelType, suffix);
    }

    internal static void AppendIgnoreNull(SharedIndentedBuilder code, int indent) =>
        code.AppendLineAt(
            indent,
            "[global::System.Text.Json.Serialization.JsonIgnore(Condition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]"
        );

    internal static void AppendJsonProperty(
        SharedIndentedBuilder code,
        int indent,
        string name,
        int order
    )
    {
        var jsonName = char.ToLowerInvariant(name[0]) + name.Substring(1);
        code.AppendLineAt(
            indent,
            "[global::System.Text.Json.Serialization.JsonPropertyName(\"" + jsonName + "\")]"
        );
        code.AppendLineAt(
            indent,
            "[global::System.Text.Json.Serialization.JsonPropertyOrder(" + order + ")]"
        );
    }

    internal static void AppendIgnoreDefault(SharedIndentedBuilder code, int indent) =>
        code.AppendLineAt(
            indent,
            "[global::System.Text.Json.Serialization.JsonIgnore(Condition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]"
        );
}
