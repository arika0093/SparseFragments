using SparseTypes.Generator.Shared;

namespace SparseTypes.Tests.Generation;

public sealed class OperationOrderingTests
{
    [Test]
    [Arguments("", "")]
    [Arguments(" \n\t \n", " \n\t \n")]
    [Arguments("    public int P;", "    public int P;")]
    [Arguments(
        "    private int C;\n\n    public int A;\n\t\n    internal int B;\n",
        "    public int A;\n    internal int B;\n    private int C;\n"
    )]
    [Arguments(
        "  private int A;\r\n  internal int B;\r\n",
        "  internal int B;\r\n  private int A;\r\n"
    )]
    [Arguments(
        "\u2003private int C;\n\u2003internal int B;",
        "\u2003internal int B;\n\u2003private int C;"
    )]
    [Arguments(
        "    /// doc\n    private int C;\n    /// next\n    public int A;\n",
        "    public int A;\n    /// doc\n    /// next\n    private int C;\n"
    )]
    [Arguments(
        "    private class C\n    {\n        public int P;\n        /// nested\n    }\n    internal int B;\n",
        "    internal int B;\n    private class C\n    {\n        public int P;\n        /// nested\n    }\n"
    )]
    public void PreservesExistingWhitespaceAndBlockBoundaries(string source, string expected)
    {
        SparseOperationOrdering.ReorderInternalFirst(source).ShouldBe(expected);
        var code = new SharedIndentedBuilder(CancellationToken.None);
        SparseOperationOrdering.AppendInternalFirst(code, source);
        code.ToString().ShouldBe(expected);
    }

    [Test]
    public void StreamingPreservesTheDestinationLineStateAndIndentation()
    {
        var code = new SharedIndentedBuilder(CancellationToken.None) { IndentOffset = 1 };
        code.Append("prefix");
        SparseOperationOrdering.AppendInternalFirst(code, "  private int C;\n  internal int B;");
        code.Append("suffix");
        code.ToString().ShouldBe("    prefix  internal int B;\n      private int C;suffix");
    }

    [Test]
    public void KeepsRelativeOrderWithinEachAccessGroup()
    {
        const string source =
            "  private int C;\n  internal int B;\n  public int A;\n  private int F;\n  internal int E;\n  public int D;";
        SparseOperationOrdering
            .ReorderInternalFirst(source)
            .ShouldBe(
                "  public int A;\n  public int D;\n  internal int B;\n  internal int E;\n  private int C;\n  private int F;"
            );
    }

    [Test]
    public void TextWithoutMemberPrefixesKeepsItsOriginalInstance()
    {
        const string source = "    publicly = 1;\n    internalValue = 2;\n    // private marker\n";
        SparseOperationOrdering.ReorderInternalFirst(source).ShouldBeSameAs(source);
    }
}
