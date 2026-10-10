using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Scalar and custom-strategy member rebase for generated patches.</summary>
internal static class SparseFragmentPatchScalarRebaseEmitter
{
    internal static void AppendCustomStrategyMemberRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string field,
        string kind,
        string baseMember,
        string desiredMember,
        string currentMember,
        string conflict,
        string conflictKind,
        int indent,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var operationType =
            SparseFragmentPatchEmitter.Operation(dialect)
            + "<"
            + SparseFragmentPatchEmitter.GetMemberValueType(dialect, member)
            + ">";
        // A member-level rebase policy wins over the merge strategy's TryRebase;
        // the strategy still owns Merge. Redacted members replay or fail whole.
        var reconcilerField =
            "Fragment." + SparseFragmentPatchEmitter.GetMergeStrategyField(dialect, member);
        var reconcilerFallback = "The custom merge strategy could not rebase the member.";
        if (member.RebasePolicyType is not null)
        {
            reconcilerField =
                "Fragment." + SparseFragmentPatchEmitter.GetRebasePolicyField(dialect, member);
            reconcilerFallback = "The custom rebase policy could not rebase the member.";
        }
        var reconcilerPathLit = SymbolDisplay.FormatLiteral(member.Property.Name, true);
        code.AppendLineAt(
            indent,
            "if (local."
                + field
                + ".Kind != "
                + kind
                + ".Keep && __SparseIsRedacted(options, "
                + reconcilerPathLit
                + "))"
        );
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(indent + 1, "if (__SparseRejectsRedacted(options))");
        code.AppendLineAt(indent + 1, "{");
        SparseRebaseOptionEmitter.AppendRedactedConflict(
            code,
            indent + 2,
            dialect.RuntimeNamespace,
            conflict,
            dialect,
            "__SparseRootPath.Member(" + reconcilerPathLit + ")",
            "conflicts"
        );
        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(indent + 1, "else");
        code.AppendLineAt(indent + 1, "{");
        code.AppendLineAt(indent + 2, "result." + field + " = local." + field + ";");
        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(indent, "}");
        // Custom strategies observe every member edit (Set and Remove): the presence-aware
        // TryRebase(Optional<T>, ...) SPI can represent a missing rebased state, so unlike the
        // previous T?-based SPI there is no need to route Remove through the scalar fallback.
        code.AppendLineAt(
            indent,
            "if (local."
                + field
                + ".Kind != "
                + kind
                + ".Keep && !__SparseIsRedacted(options, "
                + reconcilerPathLit
                + "))"
        );
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(indent + 1, "var " + baseMember + " = baseFragment." + name + ";");
        code.AppendLineAt(indent + 1, "var " + currentMember + " = currentFragment." + name + ";");
        code.AppendLineAt(
            indent + 1,
            "var " + desiredMember + " = local." + field + ".Apply(" + baseMember + ");"
        );
        var strategyField = reconcilerField;
        var strategyFallback = reconcilerFallback;
        code.AppendLineAt(
            indent + 1,
            "if ("
                + strategyField
                + ".TryRebase("
                + baseMember
                + ", "
                + desiredMember
                + ", "
                + currentMember
                + ", out var rebasedValue, out var reason))"
        );
        code.AppendLineAt(indent + 1, "{");
        code.AppendLineAt(
            indent + 2,
            "if (!((!rebasedValue.IsPresent && !"
                + currentMember
                + ".IsPresent) || (rebasedValue.IsPresent && "
                + currentMember
                + ".IsPresent && "
                + strategyField
                + ".AreEqual("
                + currentMember
                + ".Value, rebasedValue.Value))))"
        );
        code.AppendLineAt(indent + 2, "{");
        code.AppendLineAt(indent + 3, "if (rebasedValue.IsPresent)");
        code.AppendLineAt(indent + 3, "{");
        code.AppendLineAt(
            indent + 4,
            "result." + field + " = " + operationType + ".Set(rebasedValue.Value);"
        );
        code.AppendLineAt(indent + 3, "}");
        code.AppendLineAt(indent + 3, "else");
        code.AppendLineAt(indent + 3, "{");
        code.AppendLineAt(indent + 4, "result." + field + " = " + operationType + ".Remove;");
        code.AppendLineAt(indent + 3, "}");
        code.AppendLineAt(indent + 2, "}");
        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(indent + 1, "else");
        code.AppendLineAt(indent + 1, "{");
        code.AppendLineAt(
            indent + 2,
            "conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                + "), "
                + conflictKind
                + ".CustomStrategy, __SparseMember("
                + baseMember
                + "), __SparseMember("
                + desiredMember
                + "), __SparseMember("
                + currentMember
                + "), reason ?? \""
                + strategyFallback
                + "\"));"
        );
        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(indent, "}");
    }

    internal static void EmitScalarRebase(
        SharedIndentedBuilder code,
        string field,
        string memberName,
        string baseMember,
        string desiredMember,
        string currentMember,
        string equality,
        string conflictKind,
        string conflict,
        int indent
    )
    {
        code.AppendLineAt(
            indent,
            "if (" + equality + "(" + baseMember + ", " + currentMember + "))"
        );
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(indent + 1, "result." + field + " = local." + field + ";");
        code.AppendLineAt(indent, "}");
        code.AppendLineAt(
            indent,
            "else if (!"
                + equality
                + "("
                + desiredMember
                + ", "
                + currentMember
                + ") && !"
                + equality
                + "("
                + desiredMember
                + ", "
                + baseMember
                + "))"
        );
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(
            indent + 1,
            "conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + SymbolDisplay.FormatLiteral(memberName, true)
                + "), "
                + conflictKind
                + ", __SparseMember("
                + baseMember
                + "), __SparseMember("
                + desiredMember
                + "), __SparseMember("
                + currentMember
                + "), \"The member conflicts with a concurrent change.\"));"
        );
        code.AppendLineAt(indent, "}");
    }
}
