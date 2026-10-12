using static SparseFragments.Generator.Shared.SparseChangeSetBasicsEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetMatchEmitter;

namespace SparseFragments.Generator.Shared;

/// <summary>Projects and reuses sparse dictionary views.</summary>
internal static class SparseChangeSetDictProjectionEmitter
{
    internal static void AppendProjection(
        SharedIndentedBuilder shell,
        SparseMemberModel member,
        string prop,
        string trans,
        string cacheName,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target
    )
    {
        var code = target is null ? shell : target.ChangeSetOperations;
        var keyType = KeyTypeOf(member);
        var valueType = ValueTypeOf(member);
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var hasPatch = member.Collection.ValueType?.IsFragmentModel == true;
        var editedType = hasPatch ? ValueChangeSetOf(member) : valueType;
        // Sparse projection from canonical storage.
        if (target is null)
        {
            code.AppendLineAt(2, "private " + trans + " __SparseProject_" + member.Id + "()");
        }
        else
        {
            code.AppendLineAt(
                2,
                "/// <summary>Projects the typed dictionary transition from canonical sparse storage.</summary>"
            );
            code.AppendLineAt(
                2,
                "internal static " + trans + " __SparseProject_" + member.Id + "(ChangeSet self)"
            );
        }
        code.AppendLineAt(2, "{");
        if (target is not null)
        {
            AppendMemberStateAliases(code, member);
        }
        code.AppendLineAt(
            3,
            "if (__sparse_hasWhole) return __SparseBuild_"
                + member.Id
                + "(__SparseBefore_"
                + member.Id
                + (target is null ? "(), __SparseAfter_" : "(self), __SparseAfter_")
                + member.Id
                + (target is null ? "());" : "(self));")
        );
        code.AppendLineAt(3, "if (!" + HasField(member) + ")");
        code.AppendLineAt(3, "{");
        EmitDictEmptyReturn(code, member, trans, comparer);
        code.AppendLineAt(3, "}");
        var wholeBefore = KeyedWholeBefore(member);
        var wholeAfter = KeyedWholeAfter(member);
        if (SparseChangeSetTransitionEmitter.NeedsSnapshot(member))
        {
            wholeBefore = "__SparseSnapshot_" + member.Id + "(" + wholeBefore + ")";
            wholeAfter = "__SparseSnapshot_" + member.Id + "(" + wholeAfter + ")";
        }
        code.AppendLineAt(
            3,
            "if ("
                + KeyedWholeFlag(member)
                + ") return __SparseBuild_"
                + member.Id
                + "("
                + wholeBefore
                + ", "
                + wholeAfter
                + ");"
        );
        // Granular: derive Added/Removed/Edited maps from stored items (no full snapshots).
        EmitDictGranularReturn(
            code,
            member,
            trans,
            comparer,
            keyType,
            valueType,
            editedType,
            hasPatch
        );
        code.AppendLineAt(2, "}");
        var projectCall = target is null
            ? "__SparseProject_" + member.Id + "()"
            : target.ChangeSetOperationsType + ".__SparseProject_" + member.Id + "(this)";
        shell.AppendLineAt(
            2,
            "/// <summary>Gets the typed dictionary transition for member '"
                + member.Property.Name
                + "'.</summary>"
        );
        shell.AppendLineAt(2, "[global::System.Text.Json.Serialization.JsonIgnore]");
        shell.AppendLineAt(2, "public " + trans + " " + prop);
        shell.AppendLineAt(2, "{");
        shell.AppendLineAt(3, "get");
        shell.AppendLineAt(3, "{");
        // Whole endpoints expose mutable container snapshots and must stay independent.
        shell.AppendLineAt(
            4,
            "if (__sparse_hasWhole || " + KeyedWholeFlag(member) + ") return " + projectCall + ";"
        );
        shell.AppendLineAt(
            4,
            "return " + cacheName + " ?? (" + cacheName + " = " + projectCall + ");"
        );
        shell.AppendLineAt(3, "}");
        shell.AppendLineAt(2, "}");
    }
}
