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
                // Relocated operation bodies reach state through these
                // fields, so they are internal rather than private. The
                // typed ref accessor stays on the facade to preserve
                // in-place mutation identity.
                code.AppendLineAt(2, "internal " + type + " " + field + ";");
                code.AppendLineAt(2, "public ref " + type + " " + name + " => ref " + field + ";");
            }
            else if (SparseFragmentPatchEmitter.IsCollectionPatch(member))
            {
                var type = SparseFragmentPatchEmitter.GetCollectionPatchName(dialect, member);
                code.AppendLineAt(2, "internal " + type + "? " + field + ";");
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
                code.AppendLineAt(2, "internal " + type + "? " + field + ";");
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
        // The whole-operation field and the member-emptiness predicate stay
        // on the facade as storage, but relocated operation bodies read them
        // through internal access.
        code.AppendLineAt(
            2,
            "internal " + operation + "<Fragment?> " + dialect.WholeFieldName + ";"
        );
        code.AppendLineAt(
            2,
            "internal bool "
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
    /// <param name="target">Relocation target, or null for single-file emission.</param>
    public static void AppendPatchApplyMembers(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null
    )
    {
        if (target is not null)
        {
            code.AppendLineAt(
                2,
                "internal Fragment ApplyMembers(Fragment current) => "
                    + target.PatchOperationsType
                    + ".ApplyMembers(this, current);"
            );
            AppendPatchApplyMembersBody(target.PatchOperations, members, dialect, "self.");
            return;
        }

        AppendPatchApplyMembersBody(code, members, dialect, string.Empty);
    }

    private static void AppendPatchApplyMembersBody(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string receiver
    )
    {
        var declaration =
            receiver.Length == 0
                ? "internal Fragment ApplyMembers(Fragment current) => new Fragment"
                : "internal static Fragment ApplyMembers(Patch self, Fragment current) => new Fragment";
        code.AppendLineAt(2, declaration);
        code.AppendLineAt(2, "{");
        foreach (var member in members)
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            var field = receiver + dialect.MemberField(member);
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

    /// <summary>Aliases patch facade state as locals for relocated bodies.</summary>
    /// <remarks>
    /// Relocated Patch operations take the facade as an explicit
    /// <c>self</c> parameter. Payload projection bodies only read patch
    /// state, so plain locals preserve the legacy read semantics exactly.
    /// </remarks>
    internal static void AppendPatchSelfAliases(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        int indent = 3
    )
    {
        code.AppendLineAt(
            indent,
            "var " + dialect.WholeFieldName + " = self." + dialect.WholeFieldName + ";"
        );
        foreach (var member in members)
        {
            var field = dialect.MemberField(member);
            code.AppendLineAt(indent, "var " + field + " = self." + field + ";");
        }
    }

    /// <summary>Emits the Optional apply path.</summary>
    /// <param name="target">Relocation target, or null for single-file emission.</param>
    public static void AppendPatchOptionalApply(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string methodDeclaration,
        SparseOperationTarget? target = null
    )
    {
        if (target is not null)
        {
            var optional = SparseFragmentPatchEmitter.OptionalFragment(dialect);
            code.AppendLineAt(
                2,
                "public "
                    + optional
                    + " Apply("
                    + optional
                    + " current) => "
                    + target.PatchOperationsType
                    + ".Apply(this, current);"
            );
            AppendPatchOptionalApplyBody(
                target.PatchOperations,
                dialect,
                "internal static " + optional + " Apply(Patch self, " + optional + " current)",
                "self."
            );
            return;
        }

        AppendPatchOptionalApplyBody(code, dialect, methodDeclaration, string.Empty);
    }

    private static void AppendPatchOptionalApplyBody(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string methodDeclaration,
        string receiver
    )
    {
        var optional = SparseFragmentPatchEmitter.OptionalFragment(dialect);
        code.AppendLineAt(2, methodDeclaration);
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "current = " + receiver + dialect.WholeFieldName + ".Apply(current);");
        code.AppendLineAt(3, "if (" + receiver + dialect.MembersEmptyName + ") return current;");
        code.AppendLineAt(
            3,
            "var basis = current.IsPresent && current.Value is not null ? current.Value : new Fragment();"
        );
        code.AppendLineAt(
            3,
            "return "
                + optional
                + ".Present("
                + (receiver.Length == 0 ? "ApplyMembers(basis));" : "ApplyMembers(self, basis));")
        );
        code.AppendLineAt(2, "}");
    }
}
