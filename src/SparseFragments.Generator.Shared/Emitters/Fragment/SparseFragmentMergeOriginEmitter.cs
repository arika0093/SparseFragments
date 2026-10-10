using System;
using System.Collections.Immutable;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits origin-aware merge and apply bodies for generated fragments.</summary>
/// <remarks>
/// Split from <c>SparseFragmentMergeEmitter</c> so neither file exceeds the
/// Sonar file-size rule; the bodies are unchanged, only relocated.
/// Origins stay explanatory metadata: values compute once into locals, the
/// origin-free fast path returns them unattributed, and attribution arrays
/// are attached only when provenance is present.
/// </remarks>
internal static class SparseFragmentMergeOriginEmitter
{
    /// <summary>Appends the origin-propagating merge tail after the merged values are computed.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="receiver">Receiver prefix for the lower-priority side.</param>
    /// <param name="buildMergeValue">Merged-value expression builder.</param>
    /// <param name="origins">Origin emitter for per-element attribution.</param>
    internal static void AppendMergeWithOrigins(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string receiver,
        Func<SparseMemberModel, string, int, string> buildMergeValue,
        SparseFragmentOriginEmitter origins
    )
    {
        // Values compute once into locals so the origin-free fast path below
        // returns the same values with no attribution overhead.
        for (var position = 0; position < members.Length; position++)
        {
            var member = members[position];
            code.AppendLineAt(
                3,
                "var __sparse_value_"
                    + member.Id
                    + " = "
                    + buildMergeValue(member, receiver, position)
                    + ";"
            );
        }

        code.AppendLineAt(
            3,
            "if ("
                + receiver
                + "Origin is null && higherPriority.Origin is null && selfFallback is null && higherFallback is null && "
                + receiver
                + "__SparseMemberOrigins is null && higherPriority.__SparseMemberOrigins is null && "
                + receiver
                + "__SparseElementOrigins is null && higherPriority.__SparseElementOrigins is null)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "return new Fragment");
        code.AppendLineAt(4, "{");
        foreach (var member in members)
        {
            code.AppendIndent(5)
                .Append(SparseNaming.EscapeIdentifier(member.Property.Name))
                .Append(" = __sparse_value_")
                .Append(member.Id)
                .AppendLine(",");
        }

        code.AppendLineAt(4, "};");
        code.AppendLineAt(3, "}");
        var elementPositions = new System.Collections.Generic.List<int>();
        for (var position = 0; position < members.Length; position++)
        {
            code.AppendLineAt(
                3,
                "string? __sparse_origin_"
                    + members[position].Id
                    + " = "
                    + SparseFragmentOriginEmitter.BuildMemberOrigin(
                        members[position],
                        receiver,
                        "higherPriority.",
                        position,
                        "selfFallback",
                        "higherFallback"
                    )
                    + ";"
            );
            if (SparseFragmentOriginEmitter.HasElementOrigins(members[position]))
            {
                elementPositions.Add(position);
            }
        }

        foreach (var position in elementPositions)
        {
            var member = members[position];
            code.AppendLineAt(
                3,
                "string?[]? __sparse_elements_"
                    + member.Id
                    + " = "
                    + origins.BuildElementOrigins(
                        member,
                        receiver,
                        "higherPriority.",
                        position,
                        "__sparse_value_" + member.Id + ".Value!",
                        "selfFallback",
                        "higherFallback"
                    )
                    + ";"
            );
        }

        code.AppendLineAt(
            3,
            "return new Fragment(higherPriority.Origin ?? higherFallback ?? "
                + receiver
                + "Origin ?? selfFallback)"
        );
        code.AppendLineAt(3, "{");
        foreach (var member in members)
        {
            code.AppendIndent(4)
                .Append(SparseNaming.EscapeIdentifier(member.Property.Name))
                .Append(" = __sparse_value_")
                .Append(member.Id)
                .AppendLine(",");
        }

        code.AppendIndent(4).Append("__SparseMemberOrigins = new string?[] { ");
        for (var position = 0; position < members.Length; position++)
        {
            if (position > 0)
            {
                code.Append(", ");
            }

            code.Append("__sparse_origin_").Append(members[position].Id);
        }

        code.AppendLine(" },");
        if (elementPositions.Count == 0)
        {
            code.AppendLineAt(3, "};");
            return;
        }

        code.AppendIndent(4).Append("__SparseElementOrigins = (");
        for (var index = 0; index < elementPositions.Count; index++)
        {
            if (index > 0)
            {
                code.Append(" || ");
            }

            code.Append("__sparse_elements_")
                .Append(members[elementPositions[index]].Id)
                .Append(" is not null");
        }

        code.Append(") ? new string?[]?[] { ");
        for (var position = 0; position < members.Length; position++)
        {
            if (position > 0)
            {
                code.Append(", ");
            }

            code.Append(
                elementPositions.Contains(position)
                    ? "__sparse_elements_" + members[position].Id
                    : "null"
            );
        }

        code.AppendLine(" } : null,");
        code.AppendLineAt(3, "};");
    }

    /// <summary>Appends the origin-propagating apply tail after the applied values are computed.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="receiver">Receiver prefix for the fragment side.</param>
    /// <param name="buildApplyValue">Applied-value expression builder.</param>
    internal static void AppendApplyChangesWithOrigins(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string receiver,
        Func<SparseMemberModel, string, int, string> buildApplyValue
    )
    {
        for (var position = 0; position < members.Length; position++)
        {
            var member = members[position];
            code.AppendLineAt(
                3,
                "var __sparse_value_"
                    + member.Id
                    + " = "
                    + buildApplyValue(member, receiver, position)
                    + ";"
            );
        }

        code.AppendLineAt(
            3,
            "if ("
                + receiver
                + "Origin is null && changes.Origin is null && selfFallback is null && changesFallback is null && "
                + receiver
                + "__SparseMemberOrigins is null && changes.__SparseMemberOrigins is null && "
                + receiver
                + "__SparseElementOrigins is null && changes.__SparseElementOrigins is null)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "return new Fragment");
        code.AppendLineAt(4, "{");
        foreach (var member in members)
        {
            code.AppendIndent(5)
                .Append(SparseNaming.EscapeIdentifier(member.Property.Name))
                .Append(" = __sparse_value_")
                .Append(member.Id)
                .AppendLine(",");
        }

        code.AppendLineAt(4, "};");
        code.AppendLineAt(3, "}");
        var elementPositions = new System.Collections.Generic.List<int>();
        for (var position = 0; position < members.Length; position++)
        {
            code.AppendLineAt(
                3,
                "string? __sparse_origin_"
                    + members[position].Id
                    + " = "
                    + SparseFragmentOriginEmitter.BuildApplyOrigin(
                        members[position],
                        receiver,
                        "changes.",
                        position,
                        "selfFallback",
                        "changesFallback"
                    )
                    + ";"
            );
            if (members[position].ChildModel is null)
            {
                elementPositions.Add(position);
                code.AppendLineAt(
                    3,
                    "string?[]? __sparse_elements_"
                        + members[position].Id
                        + " = "
                        + SparseFragmentOriginEmitter.BuildApplyElements(
                            members[position],
                            receiver,
                            "changes.",
                            position
                        )
                        + ";"
                );
            }
        }

        code.AppendLineAt(
            3,
            "return new Fragment(changes.Origin ?? changesFallback ?? "
                + receiver
                + "Origin ?? selfFallback)"
        );
        code.AppendLineAt(3, "{");
        foreach (var member in members)
        {
            code.AppendIndent(4)
                .Append(SparseNaming.EscapeIdentifier(member.Property.Name))
                .Append(" = __sparse_value_")
                .Append(member.Id)
                .AppendLine(",");
        }

        code.AppendIndent(4).Append("__SparseMemberOrigins = new string?[] { ");
        for (var position = 0; position < members.Length; position++)
        {
            if (position > 0)
            {
                code.Append(", ");
            }

            code.Append("__sparse_origin_").Append(members[position].Id);
        }

        code.AppendLine(" },");
        if (elementPositions.Count == 0)
        {
            code.AppendLineAt(4, "__SparseElementOrigins = null,");
            code.AppendLineAt(3, "};");
            return;
        }

        code.AppendIndent(4).Append("__SparseElementOrigins = (");
        for (var index = 0; index < elementPositions.Count; index++)
        {
            if (index > 0)
            {
                code.Append(" || ");
            }

            code.Append("__sparse_elements_")
                .Append(members[elementPositions[index]].Id)
                .Append(" is not null");
        }

        code.Append(") ? new string?[]?[] { ");
        for (var position = 0; position < members.Length; position++)
        {
            if (position > 0)
            {
                code.Append(", ");
            }

            code.Append(
                elementPositions.Contains(position)
                    ? "__sparse_elements_" + members[position].Id
                    : "null"
            );
        }

        code.AppendLine(" } : null,");
        code.AppendLineAt(3, "};");
    }
}
