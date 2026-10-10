namespace SparseFragments.Generator.Shared;

internal static class SparseChangeSetPathEmitter
{
    internal static void Append(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null
    )
    {
        var pathType = SparseFragmentPatchEmitter.GetPathType(dialect);
        if (target is not null)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Enumerates changed member paths, descending through nested models.</summary>"
            );
            code.AppendLineAt(
                2,
                "/// <remarks>A whole-root presence transition reports the root path, replacing member paths. Exact paths; use <c>StartsWith</c> and <c>IsAncestorOf</c> for subtree queries.</remarks>"
            );
            code.AppendLineAt(2, "/// <returns>The changed member paths.</returns>");
            code.AppendLineAt(
                2,
                "public global::System.Collections.Generic.IReadOnlyList<"
                    + pathType
                    + "> EnumerateChangedPaths() => "
                    + target.ChangeSetOperationsType
                    + ".EnumerateChangedPaths(this);"
            );
            code = target.ChangeSetOperations;
            code.AppendLineAt(
                2,
                "/// <summary>Enumerates changed member paths, descending through nested models.</summary>"
            );
            code.AppendLineAt(
                2,
                "/// <remarks>A whole-root presence transition reports the root path, replacing member paths. Exact paths; use <c>StartsWith</c> and <c>IsAncestorOf</c> for subtree queries.</remarks>"
            );
            code.AppendLineAt(2, "/// <param name=\"self\">The change set to enumerate.</param>");
            code.AppendLineAt(2, "/// <returns>The changed member paths.</returns>");
            code.AppendLineAt(
                2,
                "internal static global::System.Collections.Generic.IReadOnlyList<"
                    + pathType
                    + "> EnumerateChangedPaths(ChangeSet self)"
            );
        }
        else
        {
            code.AppendLineAt(
                2,
                "/// <summary>Enumerates changed member paths, descending through nested models.</summary>"
            );
            code.AppendLineAt(
                2,
                "/// <remarks>A whole-root presence transition reports the root path, replacing member paths. Exact paths; use <c>StartsWith</c> and <c>IsAncestorOf</c> for subtree queries.</remarks>"
            );
            code.AppendLineAt(2, "/// <returns>The changed member paths.</returns>");
            code.AppendLineAt(
                2,
                "public global::System.Collections.Generic.IReadOnlyList<"
                    + pathType
                    + "> EnumerateChangedPaths()"
            );
        }
        code.AppendLineAt(2, "{");
        var enumerate = target is null ? "EnumerateChanges()" : "EnumerateChanges(self)";
        code.AppendLineAt(
            3,
            "var paths = new global::System.Collections.Generic.List<" + pathType + ">();"
        );
        code.AppendLineAt(
            3,
            "var seen = new global::System.Collections.Generic.HashSet<" + pathType + ">();"
        );
        code.AppendLineAt(3, "foreach (var change in " + enumerate + ")");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (seen.Add(change.Path)) paths.Add(change.Path);");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "paths.Sort(static (left, right) => global::System.String.CompareOrdinal(left.ToString(), right.ToString()));"
        );
        code.AppendLineAt(3, "return paths.AsReadOnly();");
        code.AppendLineAt(2, "}");
    }
}
