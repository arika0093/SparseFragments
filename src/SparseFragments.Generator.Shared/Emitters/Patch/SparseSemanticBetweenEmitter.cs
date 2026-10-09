using System;
using System.Collections.Immutable;

namespace SparseFragments.Generator.Shared;

/// <summary>
/// Product-neutral dialect for semantic <c>Between</c> emission.
/// </summary>
/// <remarks>
/// The algorithm (whole-state transitions, scalar member presence/value changes,
/// nested recursion) is shared; each generator supplies its naming and member
/// expression hooks. Public API decisions stay separate: generators may use the
/// emitted helper internally without exposing a public <c>Between</c> method.
/// </remarks>
internal readonly record struct SparseBetweenDialect(
    string RuntimeNamespace,
    string OptionalFragmentType,
    string WholeOperationType,
    string WholeFieldName,
    string MethodPrefix,
    Func<SparseMemberModel, string> MemberField,
    Func<SparseMemberModel, bool> IsScalarMember,
    Func<SparseMemberModel, string> ScalarValueType,
    Func<SparseMemberModel, string, string, string> ScalarEquality,
    Func<SparseMemberModel, string, string, string> NestedBetween
);

/// <summary>
/// Emits a semantic sparse patch between two presence-aware contribution states.
/// </summary>
/// <remarks>
/// Handles whole-state transitions (missing/present-null/present-value),
/// per-member scalar presence/value changes, and nested recursion through the
/// dialect's member hooks; unchanged members yield empty operations.
/// </remarks>
internal static class SparseSemanticBetweenEmitter
{
    public static void AppendBetweenMethod(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseBetweenDialect dialect,
        SparseOperationTarget? target = null
    )
    {
        code.CancellationToken.ThrowIfCancellationRequested();
        if (target is not null)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Derives a patch between two sparse contribution states, preserving presence exactly.</summary>"
            );
            code.AppendLineAt(
                2,
                "public static Patch "
                    + dialect.MethodPrefix
                    + "Between("
                    + dialect.OptionalFragmentType
                    + " before, "
                    + dialect.OptionalFragmentType
                    + " after) => "
                    + target.PatchOperationsType
                    + "."
                    + dialect.MethodPrefix
                    + "Between(before, after);"
            );
            AppendBetweenBody(target.PatchOperations, members, dialect, isStatic: true);
            return;
        }

        AppendBetweenBody(code, members, dialect, isStatic: false);
    }

    private static void AppendBetweenBody(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseBetweenDialect dialect,
        bool isStatic
    )
    {
        code.AppendLineAt(
            2,
            "/// <summary>Derives a patch between two sparse contribution states, preserving presence exactly.</summary>"
        );
        code.AppendLineAt(
            2,
            (isStatic ? "internal static Patch " : "public static Patch ")
                + dialect.MethodPrefix
                + "Between("
                + dialect.OptionalFragmentType
                + " before, "
                + dialect.OptionalFragmentType
                + " after)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var patch = new Patch();");
        code.AppendLineAt(3, "if (before.IsPresent != after.IsPresent)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "patch."
                + dialect.WholeFieldName
                + " = after.IsPresent ? "
                + dialect.WholeOperationType
                + ".Set(after.Value) : "
                + dialect.WholeOperationType
                + ".Remove;"
        );
        code.AppendLineAt(4, "return patch;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "if (!before.IsPresent) return patch;");
        code.AppendLineAt(
            3,
            "if (global::System.Object.ReferenceEquals(before.Value, after.Value)) return patch;"
        );
        code.AppendLineAt(3, "if (before.Value is null || after.Value is null)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "patch."
                + dialect.WholeFieldName
                + " = "
                + dialect.WholeOperationType
                + ".Set(after.Value);"
        );
        code.AppendLineAt(4, "return patch;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "var beforeFragment = before.Value!;");
        code.AppendLineAt(3, "var afterFragment = after.Value!;");
        foreach (var member in members)
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            var field = dialect.MemberField(member);
            if (dialect.IsScalarMember(member))
            {
                var operation =
                    dialect.RuntimeNamespace
                    + "FragmentOperation<"
                    + dialect.ScalarValueType(member)
                    + ">";
                var beforeValue = "beforeFragment." + name + ".Value";
                var afterValue = "afterFragment." + name + ".Value";
                var equality = dialect.ScalarEquality(member, beforeValue, afterValue);
                code.AppendLineAt(3, "patch." + field + " = !afterFragment." + name + ".IsPresent");
                code.AppendLineAt(
                    4,
                    "? (beforeFragment."
                        + name
                        + ".IsPresent ? "
                        + operation
                        + ".Remove : default("
                        + operation
                        + "))"
                );
                code.AppendLineAt(
                    4,
                    ": ((beforeFragment."
                        + name
                        + ".IsPresent && "
                        + equality
                        + ") ? default("
                        + operation
                        + ") : "
                        + operation
                        + ".Set(afterFragment."
                        + name
                        + ".Value));"
                );
            }
            else
            {
                code.AppendLineAt(
                    3,
                    "patch."
                        + field
                        + " = "
                        + dialect.NestedBetween(
                            member,
                            "beforeFragment." + name,
                            "afterFragment." + name
                        )
                        + ";"
                );
            }
        }

        code.AppendLineAt(3, "return patch;");
        code.AppendLineAt(2, "}");
    }
}
