using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static SparseFragments.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseFragments.Generator.Shared;

internal static class SparseChangeSetFindEmitter
{
    internal static void AppendShellStub(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        SparseOperationTarget target
    )
    {
        var pathType = SparseFragmentPatchEmitter.GetPathType(dialect);
        code.AppendLineAt(
            2,
            "/// <summary>Finds the entry at exactly the given path, or null when absent.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Exact paths only; ancestors and descendants never match.</remarks>"
        );
        code.AppendLineAt(
            2,
            "public ChangeInfo? Find("
                + pathType
                + " path) => "
                + target.ChangeSetOperationsType
                + ".Find(this, path);"
        );
        if (modelType is not null)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Finds the entry at exactly the given typed path, or null when absent.</summary>"
            );
            code.AppendLineAt(
                2,
                "/// <remarks>Exact paths only; ancestors and descendants never match.</remarks>"
            );
            code.AppendLineAt(
                2,
                "public ChangeInfo? Find<TValue>("
                    + SparseFragmentPatchEmitter.GetTypedPathType(dialect, modelType, "TValue")
                    + " path) => "
                    + target.ChangeSetOperationsType
                    + ".Find(this, path);"
            );
        }
    }

    internal static void Append(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        ImmutableArray<SparseMemberModel> members,
        Dictionary<int, string> propertyNames,
        SparseOperationTarget? target
    )
    {
        var pathType = SparseFragmentPatchEmitter.GetPathType(dialect);
        var receiver = target is null ? string.Empty : "ChangeSet self, ";
        var enumerate = target is null ? "EnumerateChanges()" : "EnumerateChanges(self)";
        var visibility = target is null ? "public" : "internal static";
        code.AppendLineAt(
            2,
            "/// <summary>Finds the entry at exactly the given path, or null when absent.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Exact paths only; ancestors and descendants never match.</remarks>"
        );
        code.AppendLineAt(2, visibility + " ChangeInfo? Find(" + receiver + pathType + " path)");
        code.AppendLineAt(2, "{");
        if (!dialect.PathIsValueType)
        {
            code.AppendLineAt(
                3,
                "if (path is null) throw new global::System.ArgumentNullException(nameof(path));"
            );
        }
        code.AppendLineAt(
            3,
            "if (" + (target is null ? string.Empty : "self.") + "IsEmpty) return null;"
        );
        AppendScalarLookup(code, members, propertyNames, modelType, target);
        code.AppendLineAt(3, "foreach (var change in " + enumerate + ")");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (change.Path.Equals(path)) return change;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return null;");
        code.AppendLineAt(2, "}");
        if (modelType is null)
        {
            return;
        }

        code.AppendLineAt(
            2,
            "/// <summary>Finds the entry at exactly the given typed path, or null when absent.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Exact paths only; ancestors and descendants never match. The path root and value types are checked at compile time.</remarks>"
        );
        code.AppendLineAt(
            2,
            visibility
                + " ChangeInfo? Find<TValue>("
                + receiver
                + SparseFragmentPatchEmitter.GetTypedPathType(dialect, modelType, "TValue")
                + " path)"
        );
        code.AppendLineAt(2, "{");
        if (!dialect.PathIsValueType)
        {
            code.AppendLineAt(
                3,
                "if (path is null) throw new global::System.ArgumentNullException(nameof(path));"
            );
        }
        code.AppendLineAt(
            3,
            target is null ? "return Find(path.Path);" : "return Find(self, path.Path);"
        );
        code.AppendLineAt(2, "}");
    }

    private static void AppendScalarLookup(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        Dictionary<int, string> propertyNames,
        string? modelType,
        SparseOperationTarget? target
    )
    {
        if (modelType is null || !members.Any(IsScalarChangeEntry))
        {
            return;
        }
        var receiver = target is null ? string.Empty : "self.";
        code.AppendLineAt(
            3,
            "if (path.Depth == 1 && !"
                + receiver
                + "__sparse_hasWhole && path.RootType == typeof("
                + modelType
                + "))"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "switch (path.Segments[0].Name)");
        code.AppendLineAt(4, "{");
        var onlyScalars = true;
        foreach (var member in members)
        {
            if (!IsScalarChangeEntry(member))
            {
                onlyScalars = false;
                continue;
            }
            var name = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(
                member.Property.Name,
                true
            );
            var property = receiver + SparseNaming.EscapeIdentifier(propertyNames[member.Id]);
            code.AppendLineAt(5, "case " + name + ":");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(6, "var value = " + property + ";");
            code.AppendLineAt(
                6,
                "return value.IsChanged ? __SparseCreateChangeInfo(path, value.Before, value.After) : (ChangeInfo?)null;"
            );
            code.AppendLineAt(5, "}");
        }
        if (onlyScalars)
        {
            code.AppendLineAt(5, "default: return null;");
        }
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
    }

    private static bool IsScalarChangeEntry(SparseMemberModel member) =>
        IsScalar(member) && !IsSet(member);
}
