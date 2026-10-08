using System;
using System.Collections.Immutable;
using System.Linq;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits patch storage, whole-operation handling, construction and apply paths.</summary>
internal static class SparseFragmentPatchCoreEmitter
{
    public static void AppendPatchMembers(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var runtime = dialect.RuntimeNamespace;
        foreach (var member in members)
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            var field = dialect.MemberField(member);
            if (member.ChildModel is null && !SparseFragmentPatchEmitter.IsCollectionPatch(member))
            {
                var type =
                    runtime
                    + "FragmentOperation"
                    + "<"
                    + SparseFragmentPatchEmitter.GetMemberValueType(dialect, member)
                    + ">";
                code.AppendLineAt(2, "private " + type + " " + field + ";");
                code.AppendLineAt(2, "public ref " + type + " " + name + " => ref " + field + ";");
            }
            else if (SparseFragmentPatchEmitter.IsCollectionPatch(member))
            {
                var type = SparseFragmentPatchEmitter.GetCollectionPatchName(dialect, member);
                code.AppendLineAt(2, "private " + type + "? " + field + ";");
                code.AppendLineAt(
                    2,
                    "public "
                        + type
                        + " "
                        + name
                        + " { get => "
                        + field
                        + " ??= new "
                        + type
                        + "(); set => "
                        + field
                        + " = value; }"
                );
            }
            else
            {
                var type = dialect.ChildPatchName(member);
                code.AppendLineAt(2, "private " + type + "? " + field + ";");
                code.AppendLineAt(
                    2,
                    "public "
                        + type
                        + " "
                        + name
                        + " { get => "
                        + field
                        + " ??= new "
                        + type
                        + "(); set => "
                        + field
                        + " = value; }"
                );
            }
        }
    }

    /// <summary>Emits whole-operation field, empty helpers, Set/Remove, and implicit conversion.</summary>
    public static void AppendPatchWholeOperations(
        SharedIndentedBuilder code,
        string modelType,
        string contract,
        string emptyContract,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var operation = SparseFragmentPatchEmitter.Operation(dialect);
        var kind = SparseFragmentPatchEmitter.Kind(dialect);
        var wholePrefix = SparseNaming.WholeApiPrefix(
            members.Select(static member => member.Property.Name)
        );
        code.AppendLineAt(
            2,
            "private " + operation + "<Fragment?> " + dialect.WholeFieldName + ";"
        );
        code.AppendLineAt(
            2,
            "private bool "
                + dialect.MembersEmptyName
                + " => "
                + SparseFragmentPatchEmitter.MembersEmptyExpression(members, dialect)
                + ";"
        );
        code.AppendLineAt(
            2,
            "public bool "
                + wholePrefix
                + "IsEmpty => "
                + dialect.WholeFieldName
                + ".Kind == "
                + kind
                + ".Keep && "
                + dialect.MembersEmptyName
                + ";"
        );
        code.AppendLineAt(
            2,
            "public void "
                + wholePrefix
                + "Set("
                + modelType
                + " value) => "
                + dialect.WholeFieldName
                + " = "
                + operation
                + "<Fragment?>.Set(Fragment.From(value));"
        );
        code.AppendLineAt(
            2,
            "public void "
                + wholePrefix
                + "SetNull() => "
                + dialect.WholeFieldName
                + " = "
                + operation
                + "<Fragment?>.Set(null);"
        );
        code.AppendLineAt(
            2,
            "public void "
                + wholePrefix
                + "Remove() => "
                + dialect.WholeFieldName
                + " = "
                + operation
                + "<Fragment?>.Remove;"
        );
        code.AppendLineAt(2, "internal bool __SparseIsEmpty() => " + wholePrefix + "IsEmpty;");
        code.AppendLineAt(
            2,
            "internal void __SparseSet(" + modelType + " value) => " + wholePrefix + "Set(value);"
        );
        code.AppendLineAt(2, "internal void __SparseSetNull() => " + wholePrefix + "SetNull();");
        code.AppendLineAt(2, "internal void __SparseRemove() => " + wholePrefix + "Remove();");

        code.AppendLineAt(
            2,
            "public static implicit operator Patch("
                + operation
                + "<Fragment?> operation) => new Patch { "
                + dialect.WholeFieldName
                + " = operation };"
        );
    }

    /// <summary>Emits Patch() and Patch(Fragment) construction from present members.</summary>
    public static void AppendPatchConstructor(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var operation = SparseFragmentPatchEmitter.Operation(dialect);
        code.AppendLineAt(2, "public Patch() { }");
        code.AppendLineAt(2, "public Patch(Fragment fragment)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (fragment is null) throw new global::System.ArgumentNullException(nameof(fragment));"
        );
        foreach (var member in members)
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            var field = dialect.MemberField(member);
            if (member.ChildModel is null && !SparseFragmentPatchEmitter.IsCollectionPatch(member))
                code.AppendLineAt(
                    3,
                    field
                        + " = fragment."
                        + name
                        + ".IsPresent ? "
                        + operation
                        + "<"
                        + SparseFragmentPatchEmitter.GetMemberValueType(dialect, member)
                        + ">.Set(fragment."
                        + name
                        + ".Value) : default;"
                );
            else if (SparseFragmentPatchEmitter.IsCollectionPatch(member))
            {
                var collectionPatch = SparseFragmentPatchEmitter.GetCollectionPatchName(
                    dialect,
                    member
                );
                code.AppendLineAt(3, "if (fragment." + name + ".IsPresent)");
                code.AppendLineAt(3, "{");
                code.AppendLineAt(
                    4,
                    field
                        + " = new "
                        + collectionPatch
                        + "(); "
                        + field
                        + ".Set(fragment."
                        + name
                        + ".Value!);"
                );
                code.AppendLineAt(3, "}");
            }
            else
            {
                code.AppendLineAt(3, "if (fragment." + name + ".IsPresent)");
                code.AppendLineAt(3, "{");
                code.AppendLineAt(
                    4,
                    field
                        + " = fragment."
                        + name
                        + ".Value is null ? new "
                        + dialect.ChildPatchName(member)
                        + "() : new "
                        + dialect.ChildPatchName(member)
                        + "(fragment."
                        + name
                        + ".Value);"
                );
                code.AppendLineAt(
                    4,
                    "if (fragment." + name + ".Value is null) " + field + ".__SparseSetNull();"
                );
                code.AppendLineAt(3, "}");
            }
        }
        code.AppendLineAt(2, "}");
    }

    /// <summary>Emits per-member ApplyMembers used by both Optional apply paths.</summary>
    public static void AppendPatchApplyMembers(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        code.AppendLineAt(2, "internal Fragment ApplyMembers(Fragment current) => new Fragment");
        code.AppendLineAt(2, "{");
        foreach (var member in members)
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            var field = dialect.MemberField(member);
            string expression;
            if (member.ChildModel is null && !SparseFragmentPatchEmitter.IsCollectionPatch(member))
            {
                expression = field + ".Apply(current." + name + ")";
            }
            else if (dialect.CastNestedApply && member.ChildModel is not null)
            {
                expression =
                    field
                    + " is null ? current."
                    + name
                    + " : (("
                    + dialect.NestedContract(member)
                    + ")"
                    + field
                    + ")."
                    + dialect.NestedApplyMethod
                    + "(current."
                    + name
                    + ")";
            }
            else
            {
                expression =
                    field
                    + " is null ? current."
                    + name
                    + " : "
                    + field
                    + "."
                    + dialect.NestedApplyMethod
                    + "(current."
                    + name
                    + ")";
            }
            code.AppendLineAt(3, name + " = " + expression + ",");
        }
        code.AppendLineAt(2, "};");
    }

    /// <summary>Emits the Optional apply path.</summary>
    public static void AppendPatchOptionalApply(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string methodDeclaration
    )
    {
        var optional = SparseFragmentPatchEmitter.OptionalFragment(dialect);
        code.AppendLineAt(2, methodDeclaration);
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "current = " + dialect.WholeFieldName + ".Apply(current);");
        code.AppendLineAt(3, "if (" + dialect.MembersEmptyName + ") return current;");
        code.AppendLineAt(
            3,
            "var basis = current.IsPresent && current.Value is not null ? current.Value : new Fragment();"
        );
        code.AppendLineAt(3, "return " + optional + ".Present(ApplyMembers(basis));");
        code.AppendLineAt(2, "}");
    }
}
