using System.Collections.Immutable;
using System.Linq;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits the per-member snapshot loop for payload root construction.</summary>
/// <remarks>
/// Split from <see cref="SparseChangeSetPayloadEmitter"/> to keep emitter
/// files focused: honest snapshots never redact, while before snapshots
/// redact flagged members plus everything under an ambient subtree flag.
/// </remarks>
internal static class SparseChangeSetPayloadSnapshotEmitter
{
    /// <summary>Emits the per-member snapshot loop shared by honest and redacting roots.</summary>
    /// <remarks>Honest snapshots never redact; before snapshots redact flagged members plus everything under an ambient subtree flag.</remarks>
    internal static void AppendFromFragmentMembers(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string endpoint,
        string runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        bool redactFlagged
    )
    {
        foreach (var member in members.Where(static member => !member.Property.IsJsonIgnored))
        {
            var property = SparseNaming.EscapeIdentifier(member.Property.Name);
            var redact = redactFlagged && member.RedactBefore ? "true" : "redactBefores";
            if (SparseChangeSetBasicsEmitter.IsNested(member))
            {
                var childModelType = member.ChildModel!.Value.NonNullableName;
                var childRoot =
                    childModelType
                    + "."
                    + SparseChangeSetPayloadEmitter.PayloadTypeName(
                        dialect,
                        childModelType,
                        "Root"
                    );
                var childCall = redactFlagged
                    ? childRoot + ".FromFragment(member" + member.Id + ".Value!, " + redact + ")"
                    : childRoot + ".FromFragment(member" + member.Id + ".Value!)";
                code.AppendLineAt(3, "var member" + member.Id + " = value." + property + ";");
                var valueExpression = redactFlagged
                    ? "("
                        + redact
                        + ") ? "
                        + endpoint
                        + "<"
                        + childRoot
                        + "?>.Redacted() : "
                        + SnapshotValueExpression(
                            endpoint,
                            runtime,
                            childRoot + "?",
                            "member" + member.Id,
                            childCall
                        )
                    : SnapshotValueExpression(
                        endpoint,
                        runtime,
                        childRoot + "?",
                        "member" + member.Id,
                        childCall
                    );
                code.AppendLineAt(
                    3,
                    "if (member"
                        + member.Id
                        + ".IsPresent) result.Members.Add(new "
                        + SparseChangeSetPayloadEmitter.PayloadMemberName(
                            modelType,
                            "Change",
                            member.Id
                        )
                        + " { Value = "
                        + valueExpression
                        + " });"
                );
            }
            else
            {
                var valueType = SparseChangeSetBasicsEmitter.FragmentValueType(member);
                var valueExpression = redactFlagged
                    ? "("
                        + redact
                        + ") ? "
                        + endpoint
                        + "<"
                        + valueType
                        + ">.Redacted() : "
                        + endpoint
                        + "<"
                        + valueType
                        + ">.FromOptional(value."
                        + property
                        + ")"
                    : endpoint + "<" + valueType + ">.FromOptional(value." + property + ")";
                code.AppendLineAt(
                    3,
                    "if (value."
                        + property
                        + ".IsPresent) result.Members.Add(new "
                        + SparseChangeSetPayloadEmitter.PayloadMemberName(
                            modelType,
                            "Change",
                            member.Id
                        )
                        + " { Value = "
                        + valueExpression
                        + " });"
                );
            }
        }
    }

    private static string SnapshotValueExpression(
        string endpoint,
        string runtime,
        string childRoot,
        string holder,
        string childCall
    ) =>
        endpoint
        + "<"
        + childRoot
        + ">.FromOptional("
        + runtime
        + "Optional<"
        + childRoot
        + ">.Present("
        + holder
        + ".Value is null ? null : "
        + childCall
        + "))";
}
