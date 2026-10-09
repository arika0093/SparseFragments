using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits the ChangePayload read path: validated conversion to a complete ChangeSet.</summary>
/// <remarks>Redacted or otherwise incomplete histories are rejected here rather than fabricated; baseline-discarding projection lives in <see cref="SparseChangePayloadPatchSyncEmitter"/>. The strict core (<c>FromPayloadCore</c>) and the mixed partition (<c>__SparseMixedPartition</c>) are owned by <see cref="SparseChangeSetMixedEmitter"/> so ordinary and blind members route through one seam.</remarks>
internal static class SparseChangePayloadReaderEmitter
{
    internal static void AppendFromPayload(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var endpoint = runtime + "ChangePayloadEndpoint";
        var payloadRoot = SparseChangeSetPayloadEmitter.PayloadName(modelType, "Root");
        var versionLiteral = SymbolDisplay.FormatLiteral(dialect.ChangePayloadVersion, true);
        code.AppendLineAt(
            2,
            "/// <summary>Converts a validated envelope to a complete change set.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Redacted or otherwise incomplete histories are rejected; project them with <see cref=\"ChangePayload.ToPatch\"/> instead.</remarks>"
        );
        code.AppendLineAt(2, "public static ChangeSet FromPayload(ChangePayload payload)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (payload is null) throw new global::System.ArgumentNullException(nameof(payload));"
        );
        code.AppendLineAt(
            3,
            "if (!global::System.String.Equals(payload.Version, "
                + versionLiteral
                + ", global::System.StringComparison.Ordinal)) throw new global::System.ArgumentException(\"Unsupported ChangePayload version.\", nameof(payload));"
        );
        code.AppendLineAt(3, "return FromPayloadCore(payload);");
        code.AppendLineAt(2, "}");
        // FromPayloadCore and __SparseMixedPartition are emitted by
        // SparseChangeSetMixedEmitter.AppendMixedPartition: the strict
        // conversion partitions the envelope first and rejects blind paths,
        // so there is exactly one routing seam for mixed requests.
        foreach (
            var member in members.Where(static member =>
                !member.Property.IsJsonIgnored
                && (
                    SparseChangeSetBasicsEmitter.IsKeyed(member)
                    || SparseChangeSetBasicsEmitter.IsDict(member)
                )
            )
        )
        {
            SparseChangeSetPayloadItemEmitter.AppendPayloadItemHelper(
                code,
                member,
                members,
                runtime,
                modelType
            );
        }
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static "
                + runtime
                + "Optional<Fragment?> __SparsePayloadFragment("
                + endpoint
                + "<"
                + payloadRoot
                + "> endpoint)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (endpoint is null) throw new global::System.ArgumentException(\"A root endpoint is required.\", nameof(endpoint));"
        );
        code.AppendLineAt(3, "var root = endpoint.ToOptional();");
        code.AppendLineAt(
            3,
            "return !root.IsPresent ? "
                + runtime
                + "Optional<Fragment?>.Missing : "
                + runtime
                + "Optional<Fragment?>.Present(root.Value?.ToFragment());"
        );
        code.AppendLineAt(2, "}");
    }
}
