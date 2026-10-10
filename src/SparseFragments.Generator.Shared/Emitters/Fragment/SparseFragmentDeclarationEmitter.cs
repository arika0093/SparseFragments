using System.Collections.Immutable;
using System.Linq;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits the generated declaration shell: fragment type, storage/members and builder API.</summary>
internal sealed class SparseFragmentDeclarationEmitter(
    string optional,
    string mergeStrategyFieldPrefix,
    string? rebasePolicyFieldPrefix = null,
    SparseFragmentOriginEmitter? originEmitter = null
)
{
    private string Optional { get; } = optional;
    private string MergeStrategyFieldPrefix { get; } = mergeStrategyFieldPrefix;

    private SparseFragmentOriginEmitter? OriginEmitter { get; } = originEmitter;

    public string MergeStrategyField(SparseMemberModel member) =>
        SparseFragmentEmitHelpers.MergeStrategyField(MergeStrategyFieldPrefix, member);

    public string RebasePolicyField(SparseMemberModel member) =>
        (rebasePolicyFieldPrefix ?? SparseWellKnownNames.RebasePolicyFieldPrefix) + member.Id;

    public static void AppendDeclaration(
        SharedIndentedBuilder code,
        string fragmentInterface,
        string deepCloneable,
        System.Action<SharedIndentedBuilder>? appendAttributes = null,
        string? advancedInterface = null,
        string accessibility = "public"
    )
    {
        code.CancellationToken.ThrowIfCancellationRequested();
        code.AppendLineAt(
            1,
            "/// <summary>A sparse, presence-aware representation of this model.</summary>"
        );
        appendAttributes?.Invoke(code);
        code.AppendLineAt(1, accessibility + " sealed class Fragment");
        code.AppendLineAt(1, "{");
        code.AppendLineAt(2, "/// <summary>Initializes a new empty fragment.</summary>");
        code.AppendLineAt(2, "public Fragment() { }");
        code.AppendLine();
    }

    public void AppendMembers(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string mergeStrategy,
        System.Action<SharedIndentedBuilder>? appendMemberAttributes = null,
        string? rebasePolicyBase = null,
        System.Func<SparseMemberModel, string>? rebasePolicyField = null
    )
    {
        // Public surface first: member slots, then IsEmpty. Internal strategy
        // caches move to AppendMemberCaches so no public member trails them.
        foreach (var member in members)
        {
            code.CancellationToken.ThrowIfCancellationRequested();
            appendMemberAttributes?.Invoke(code);
            code.AppendLineAt(
                2,
                "/// <summary>Gets the sparse value for '" + member.Property.Name + "'.</summary>"
            );
            code.AppendIndent(2)
                .Append("public ")
                .Append(Optional)
                .Append("<")
                .Append(SparseFragmentEmitHelpers.FragmentValueType(member))
                .Append("> ")
                .Append(SparseNaming.EscapeIdentifier(member.Property.Name))
                .AppendLine(" { get; init; }");
        }
        code.AppendLine();
        code.AppendLineAt(
            2,
            "/// <summary>Whether this fragment has no present members.</summary>"
        );
        code.AppendIndent(2)
            .Append("public bool IsEmpty => ")
            .Append(
                members.Length == 0
                    ? "true"
                    : string.Join(
                        " && ",
                        members.Select(member =>
                            "!" + SparseNaming.EscapeIdentifier(member.Property.Name) + ".IsPresent"
                        )
                    )
            )
            .AppendLine(";");
        code.AppendLine();
        // Internal caches are emitted separately via AppendMemberCaches so the
        // public fragment surface stays ahead of them.
    }

    /// <summary>Emits internal strategy caches trailing the public fragment surface.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="mergeStrategy">Merge strategy base type.</param>
    /// <param name="rebasePolicyBase">Rebase policy base type.</param>
    /// <param name="rebasePolicyField">Rebase policy field resolver.</param>
    public void AppendMemberCaches(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string mergeStrategy,
        string? rebasePolicyBase = null,
        System.Func<SparseMemberModel, string>? rebasePolicyField = null
    )
    {
        foreach (var member in members.Where(static member => member.MergeStrategyType is not null))
        {
            code.AppendIndent(2)
                .Append("internal static readonly ")
                .Append(mergeStrategy)
                .Append("<")
                .Append(member.Property.Type.Name)
                .Append("> ")
                .Append(MergeStrategyField(member))
                .Append(" = new ")
                .Append(member.MergeStrategyType!.Value.Name)
                .AppendLine("();");
        }
        foreach (
            var member in members.Where(static member => member.ComparisonComparerType is not null)
        )
        {
            code.AppendIndent(2)
                .Append("internal static readonly ")
                .Append(member.ComparisonComparerType!.Value.Name)
                .Append(" ")
                .Append(SparseFragmentEmitHelpers.ComparisonComparerField(member))
                .Append(" = new ")
                .Append(member.ComparisonComparerType.Value.Name)
                .AppendLine("();");
        }
        foreach (var member in members.Where(static member => member.RebasePolicyType is not null))
        {
            if (rebasePolicyBase is null)
            {
                throw new System.InvalidOperationException(
                    "A rebase policy base type is required to emit member rebase policies."
                );
            }

            code.AppendIndent(2)
                .Append("internal static readonly ")
                .Append(rebasePolicyBase)
                .Append("<")
                .Append(member.Property.Type.Name)
                .Append("> ")
                .Append(
                    rebasePolicyField is null
                        ? RebasePolicyField(member)
                        : rebasePolicyField(member)
                )
                .Append(" = new ")
                .Append(member.RebasePolicyType!.Value.Name)
                .AppendLine("();");
        }
    }

    public void AppendBuilder(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string accessibility = "public"
    )
    {
        code.CancellationToken.ThrowIfCancellationRequested();
        code.AppendLineAt(1, "/// <summary>A mutable builder for a generated fragment.</summary>");
        code.AppendLineAt(1, accessibility + " sealed class FragmentBuilder");
        code.AppendLineAt(1, "{");
        // Public surface first: staged references, construction, then Build.
        // The seeding constructor and backing fields trail as internals.
        foreach (var member in members)
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            var field = "__sparse_builder_member_" + member.Id;
            code.AppendLineAt(
                2,
                "/// <summary>Gets a mutable reference to the staged value for '"
                    + member.Property.Name
                    + "'.</summary>"
            );
            code.AppendIndent(2)
                .Append("public ref ")
                .Append(Optional)
                .Append("<")
                .Append(SparseFragmentEmitHelpers.FragmentValueType(member))
                .Append("> ")
                .Append(name)
                .Append(" => ref ")
                .Append(field)
                .AppendLine(";");
        }

        code.AppendLine();
        code.AppendLineAt(2, "/// <summary>Initializes a new empty builder.</summary>");
        code.AppendLineAt(2, "public FragmentBuilder() { }");
        code.AppendLineAt(2, "/// <summary>Builds the staged fragment.</summary>");
        code.AppendLineAt(2, "/// <returns>The built fragment.</returns>");
        if (OriginEmitter is null)
        {
            code.AppendLineAt(2, "public Fragment Build() => new()");
        }
        else
        {
            code.AppendLineAt(2, "public Fragment Build() => new(__sparse_origin)");
        }

        code.AppendLineAt(2, "{");
        foreach (var member in members)
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            code.AppendIndent(3).Append(name).Append(" = ").Append(name).AppendLine(",");
        }

        if (OriginEmitter is not null)
        {
            code.AppendLineAt(3, "__SparseMemberOrigins = __sparse_member_origins,");
            code.AppendLineAt(3, "__SparseElementOrigins = __sparse_element_origins,");
        }

        code.AppendLineAt(2, "};");
        code.AppendLineAt(2, "internal FragmentBuilder(Fragment fragment)");
        code.AppendLineAt(2, "{");
        foreach (var member in members)
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            code.AppendIndent(3).Append(name).Append(" = fragment.").Append(name).AppendLine(";");
        }

        if (OriginEmitter is not null)
        {
            SparseFragmentOriginEmitter.AppendBuilderCapture(code);
        }

        code.AppendLineAt(2, "}");
        foreach (var member in members)
        {
            var field = "__sparse_builder_member_" + member.Id;
            code.AppendIndent(2)
                .Append("private ")
                .Append(Optional)
                .Append("<")
                .Append(SparseFragmentEmitHelpers.FragmentValueType(member))
                .Append("> ")
                .Append(field)
                .AppendLine(";");
        }

        if (OriginEmitter is not null)
        {
            SparseFragmentOriginEmitter.AppendBuilderFields(code);
        }
        code.AppendLineAt(1, "}");
    }
}
