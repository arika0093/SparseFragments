namespace SparseFragments.Generator.Shared;

/// <summary>Small shared primitives for fragment-stage emitters.</summary>
internal static class SparseFragmentEmitHelpers
{
    /// <summary>EditorBrowsable attribute hiding generated-code plumbing from IntelliSense.</summary>
    /// <remarks>
    /// Emitted <c>internal</c> members are only called by other generated
    /// code, yet they stay visible inside the consuming assembly. Every such
    /// member gets this attribute instead of relying on accessibility alone.
    /// </remarks>
    internal const string BrowsableNever =
        "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]";

    /// <summary>Appends an internal generated member hidden from IntelliSense.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="indent">Indent level.</param>
    /// <param name="declaration">The internal member declaration.</param>
    internal static void AppendInternal(SharedIndentedBuilder code, int indent, string declaration)
    {
        code.AppendLineAt(indent, BrowsableNever);
        code.AppendLineAt(indent, declaration);
    }

    internal static string FragmentValueType(SparseMemberModel member) =>
        member.ChildModel is null ? member.Property.Type.Name : member.ChildFragmentType + "?";

    internal static string MergeStrategyField(string prefix, SparseMemberModel member) =>
        prefix + member.Id;

    internal static string ComparisonComparerField(SparseMemberModel member) =>
        "__sparse_comparison_" + member.Id;

    internal static void AppendCloneContext(
        SharedIndentedBuilder code,
        int indent,
        string cloneContext,
        string referenceComparer
    ) =>
        code.AppendLineAt(
            indent,
            "var " + cloneContext + " = " + referenceComparer + ".CreateCloneContext();"
        );

    internal static void AppendNullGuard(SharedIndentedBuilder code, int indent, string variable)
    {
        code.AppendLineAt(indent, "if (" + variable + " is null)");
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(
            indent + 1,
            "throw new global::System.ArgumentNullException(nameof(" + variable + "));"
        );
        code.AppendLineAt(indent, "}");
    }
}
