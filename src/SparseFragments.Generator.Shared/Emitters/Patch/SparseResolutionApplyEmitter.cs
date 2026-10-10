using System.Collections.Immutable;
using static SparseFragments.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseFragments.Generator.Shared;

/// <summary>Fragment applier for path-based conflict resolution (issue #203).</summary>
/// <remarks>
/// Emits <c>__SparseTryApplyResolution</c>, which writes one resolved
/// presence-aware value into a fragment subtree addressed by a canonical
/// <c>SparsePath</c>. Everything funnels through <c>FragmentBuilder</c> copies and cloned
/// collection containers, so shared rebase inputs are never mutated. Scalar and
/// nested slots use the known fragment value types with checked conversions (no
/// unchecked casts); keyed and dictionary entries are located by typed key
/// comparison (never display text), and nested or element leaves recurse through
/// the owning change set's own applier, so no emitter needs another model's
/// member list. Product specifics stay on the existing patch dialect (runtime
/// namespace for segments, path type for signatures).
/// </remarks>
internal static class SparseResolutionApplyEmitter
{
    internal static void AppendForwarderShell(
        SharedIndentedBuilder shell,
        string pathType,
        string conflict,
        string optionalObject,
        SparseOperationTarget? target
    )
    {
        _ = conflict;
        _ = optionalObject;
        if (target is null)
        {
            return;
        }

        shell.AppendLineAt(
            2,
            "/// <summary>Applies one resolved value to a fragment subtree (resolution infrastructure).</summary>"
        );
        shell.AppendLineAt(
            2,
            "/// <param name=\"fragment\">The fragment subtree to update.</param>"
        );
        shell.AppendLineAt(
            2,
            "/// <param name=\"path\">Canonical path addressing the value.</param>"
        );
        shell.AppendLineAt(
            2,
            "/// <param name=\"offset\">The segment this level consumes.</param>"
        );
        shell.AppendLineAt(
            2,
            "/// <param name=\"desired\">The presence-aware value to write.</param>"
        );
        shell.AppendLineAt(2, "/// <param name=\"updated\">The updated fragment.</param>");
        shell.AppendLineAt(2, "/// <param name=\"error\">A value-free reason on failure.</param>");
        shell.AppendLineAt(
            2,
            "/// <returns><see langword=\"true\"/> when the value applied.</returns>"
        );
        shell.AppendLineAt(
            2,
            "internal static bool __SparseTryApplyResolution(Fragment fragment, "
                + pathType
                + " path, int offset, "
                + optionalObject
                + " desired, out Fragment? updated, out string? error) => "
                + target.ChangeSetOperationsType
                + ".__SparseTryApplyResolution(fragment, path, offset, desired, out updated, out error);"
        );
    }

    internal static void AppendApplier(
        SharedIndentedBuilder body,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment,
        string optionalObject,
        string conflict,
        string pathType,
        string? modelType,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target
    )
    {
        _ = optionalFragment;
        _ = conflict;
        _ = modelType;
        var segmentKind = runtime + "SparsePathSegmentKind";
        body.AppendLineAt(
            2,
            "/// <summary>Applies one resolved value to a fragment subtree (resolution infrastructure).</summary>"
        );
        body.AppendLineAt(
            2,
            "internal static bool __SparseTryApplyResolution("
                + "Fragment fragment, "
                + pathType
                + " path, int offset, "
                + optionalObject
                + " desired, out Fragment? updated, out string? error)"
        );
        body.AppendLineAt(2, "{");
        body.AppendLineAt(3, "updated = null;");
        body.AppendLineAt(3, "error = null;");
        body.AppendLineAt(3, "if (path is null || offset < 0 || offset >= path.Depth)");
        body.AppendLineAt(3, "{");
        body.AppendLineAt(4, "error = \"The resolution path is empty.\";");
        body.AppendLineAt(4, "return false;");
        body.AppendLineAt(3, "}");
        body.AppendLineAt(3, "var head = path.Segments[offset];");
        body.AppendLineAt(3, "if (head.Kind != " + segmentKind + ".Member)");
        body.AppendLineAt(3, "{");
        body.AppendLineAt(4, "error = \"The resolution path must start with a member segment.\";");
        body.AppendLineAt(4, "return false;");
        body.AppendLineAt(3, "}");
        body.AppendLineAt(3, "var builder = fragment.ToBuilder();");
        foreach (var member in members)
        {
            AppendMemberBranch(body, member, runtime, segmentKind, dialect);
        }

        body.AppendLineAt(3, "error = \"Unknown member '\" + head.Name + \"'.\";");
        body.AppendLineAt(3, "return false;");
        body.AppendLineAt(2, "}");
        SparseResolutionCollectionEmitter.AppendKeyHelpers(body, members);
    }

    private static void AppendMemberBranch(
        SharedIndentedBuilder body,
        SparseMemberModel member,
        string runtime,
        string segmentKind,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var esc = SparseNaming.EscapeIdentifier(member.Property.Name);
        var name = member.Property.Name;
        var literal = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(name, true);
        body.AppendLineAt(
            3,
            "if (head.Kind == "
                + segmentKind
                + ".Member && global::System.StringComparer.Ordinal.Equals(head.Name, "
                + literal
                + "))"
        );
        body.AppendLineAt(3, "{");
        if (IsNested(member) && !member.ChildIsStructural)
        {
            AppendNestedBranch(body, member, esc, name, dialect);
        }
        else if (IsKeyed(member))
        {
            SparseResolutionCollectionEmitter.AppendKeyedBranch(body, member, esc, name, runtime);
        }
        else if (IsDict(member))
        {
            SparseResolutionDictEmitter.AppendDictBranch(body, member, esc, name, runtime);
        }
        else if (IsSet(member) || IsCollection(member))
        {
            AppendWholeOnlyBranch(body, member, esc, name, runtime, "Member");
        }
        else if (IsNested(member))
        {
            AppendWholeOnlyBranch(body, member, esc, name, runtime, "Structural member");
        }
        else
        {
            AppendScalarBranch(body, member, esc, name, runtime);
        }

        body.AppendLineAt(3, "}");
    }

    private static bool IsCollection(SparseMemberModel member) =>
        SparseKeyedCollectionEmitter.IsCollectionPatch(member);

    internal static string NonNullable(string typeName) =>
        typeName.EndsWith("?", StringComparison.Ordinal)
            ? typeName.Substring(0, typeName.Length - 1)
            : typeName;

    private static void AppendScalarBranch(
        SharedIndentedBuilder body,
        SparseMemberModel member,
        string esc,
        string name,
        string runtime
    )
    {
        var valueType = FragmentValueType(member);
        var optional = runtime + "Optional<" + valueType + ">";
        body.AppendLineAt(4, "if (offset + 1 != path.Depth)");
        body.AppendLineAt(4, "{");
        body.AppendLineAt(5, "error = \"Member '" + name + "' has no nested paths.\";");
        body.AppendLineAt(5, "return false;");
        body.AppendLineAt(4, "}");
        AppendCheckedSlot(body, "builder." + esc, optional, valueType, name, member.Id);
        body.AppendLineAt(4, "updated = builder.Build();");
        body.AppendLineAt(4, "return true;");
    }

    private static void AppendWholeOnlyBranch(
        SharedIndentedBuilder body,
        SparseMemberModel member,
        string esc,
        string name,
        string runtime,
        string label
    )
    {
        var valueType = FragmentValueType(member);
        var optional = runtime + "Optional<" + valueType + ">";
        body.AppendLineAt(4, "if (offset + 1 != path.Depth)");
        body.AppendLineAt(4, "{");
        body.AppendLineAt(
            5,
            "error = \""
                + label
                + " '"
                + name
                + "' resolves as a whole; deeper paths are not supported.\";"
        );
        body.AppendLineAt(5, "return false;");
        body.AppendLineAt(4, "}");
        AppendCheckedSlot(body, "builder." + esc, optional, valueType, name, member.Id);
        body.AppendLineAt(4, "updated = builder.Build();");
        body.AppendLineAt(4, "return true;");
    }

    internal static void AppendCheckedSlot(
        SharedIndentedBuilder body,
        string slot,
        string optional,
        string valueType,
        string name,
        int id
    )
    {
        var checkType = NonNullable(valueType);
        body.AppendLineAt(4, "if (!desired.IsPresent)");
        body.AppendLineAt(4, "{");
        body.AppendLineAt(5, slot + " = " + optional + ".Missing;");
        body.AppendLineAt(4, "}");
        body.AppendLineAt(4, "else if (desired.Value is null)");
        body.AppendLineAt(4, "{");
        body.AppendLineAt(
            5,
            "if (typeof("
                + checkType
                + ").IsValueType && global::System.Nullable.GetUnderlyingType(typeof("
                + checkType
                + ")) is null)"
        );
        body.AppendLineAt(5, "{");
        body.AppendLineAt(6, "error = \"Member '" + name + "' does not accept null.\";");
        body.AppendLineAt(6, "return false;");
        body.AppendLineAt(5, "}");
        body.AppendLineAt(5, slot + " = " + optional + ".Present(default(" + checkType + ")!);");
        body.AppendLineAt(4, "}");
        body.AppendLineAt(4, "else if (desired.Value is " + checkType + " typed" + id + ")");
        body.AppendLineAt(4, "{");
        body.AppendLineAt(5, slot + " = " + optional + ".Present(typed" + id + ");");
        body.AppendLineAt(4, "}");
        body.AppendLineAt(4, "else");
        body.AppendLineAt(4, "{");
        body.AppendLineAt(
            5,
            "error = \"The resolution value for member '" + name + "' has an unsupported type.\";"
        );
        body.AppendLineAt(5, "return false;");
        body.AppendLineAt(4, "}");
    }

    private static void AppendNestedBranch(
        SharedIndentedBuilder body,
        SparseMemberModel member,
        string esc,
        string name,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var childFragment = member.ChildFragmentType ?? "object";
        var childChangeSet = ChildChangeSet(member, dialect);
        var childModel = member.ChildModel?.NonNullableName ?? "object";
        var childOptional = dialect.RuntimeNamespace + "Optional<" + childFragment + "?>";
        body.AppendLineAt(4, "if (offset + 1 == path.Depth)");
        body.AppendLineAt(4, "{");
        body.AppendLineAt(5, "if (!desired.IsPresent)");
        body.AppendLineAt(5, "{");
        body.AppendLineAt(6, "builder." + esc + " = " + childOptional + ".Missing;");
        body.AppendLineAt(5, "}");
        body.AppendLineAt(5, "else if (desired.Value is null)");
        body.AppendLineAt(5, "{");
        body.AppendLineAt(
            6,
            "builder." + esc + " = " + childOptional + ".Present((" + childFragment + "?)null);"
        );
        body.AppendLineAt(5, "}");
        body.AppendLineAt(
            5,
            "else if (desired.Value is " + childFragment + " whole" + member.Id + ")"
        );
        body.AppendLineAt(5, "{");
        body.AppendLineAt(
            6,
            "builder." + esc + " = " + childOptional + ".Present(whole" + member.Id + ");"
        );
        body.AppendLineAt(5, "}");
        body.AppendLineAt(
            5,
            "else if (desired.Value is " + childModel + " model" + member.Id + ")"
        );
        body.AppendLineAt(5, "{");
        body.AppendLineAt(
            6,
            "builder."
                + esc
                + " = "
                + childOptional
                + ".Present("
                + childFragment
                + ".From(model"
                + member.Id
                + "));"
        );
        body.AppendLineAt(5, "}");
        body.AppendLineAt(5, "else");
        body.AppendLineAt(5, "{");
        body.AppendLineAt(
            6,
            "error = \"The resolution value for member '" + name + "' has an unsupported type.\";"
        );
        body.AppendLineAt(6, "return false;");
        body.AppendLineAt(5, "}");
        body.AppendLineAt(5, "updated = builder.Build();");
        body.AppendLineAt(5, "return true;");
        body.AppendLineAt(4, "}");
        body.AppendLineAt(4, "var child" + member.Id + " = builder." + esc + ";");
        body.AppendLineAt(
            4,
            "if (!child" + member.Id + ".IsPresent || child" + member.Id + ".Value is null)"
        );
        body.AppendLineAt(4, "{");
        body.AppendLineAt(
            5,
            "error = \"Member '" + name + "' is absent and cannot resolve deeper paths.\";"
        );
        body.AppendLineAt(5, "return false;");
        body.AppendLineAt(4, "}");
        body.AppendLineAt(
            4,
            "if (!"
                + childChangeSet
                + ".__SparseTryApplyResolution(child"
                + member.Id
                + ".Value, path, offset + 1, desired, out var updatedChild"
                + member.Id
                + ", out error)) return false;"
        );
        body.AppendLineAt(4, "if (updatedChild" + member.Id + " is null)");
        body.AppendLineAt(4, "{");
        body.AppendLineAt(5, "error = \"The nested resolution produced no fragment.\";");
        body.AppendLineAt(5, "return false;");
        body.AppendLineAt(4, "}");
        body.AppendLineAt(
            4,
            "builder." + esc + " = " + childOptional + ".Present(updatedChild" + member.Id + ");"
        );
        body.AppendLineAt(4, "updated = builder.Build();");
        body.AppendLineAt(4, "return true;");
    }
}
