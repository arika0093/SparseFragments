using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Shared;

public sealed class CodeRewriteBoundaryTests
{
    [Test]
    [Arguments("a 'a' a", "b 'a' b")]
    [Arguments("a \"a\" a", "b \"a\" b")]
    [Arguments("a \"\\\"a\" a", "b \"\\\"a\" b")]
    [Arguments("a '\\'' a", "b '\\'' b")]
    [Arguments("a @\"a\" a", "b @\"a\" b")]
    [Arguments("a @\"a \"\"a\"\"\" a", "b @\"a \"\"a\"\"\" b")]
    [Arguments("a // a\na", "b // a\nb")]
    [Arguments("a /* a */ a", "b /* a */ b")]
    [Arguments("a #region a\na", "b #region a\nb")]
    [Arguments("a $\"a {a} a\" a", "b $\"a {b} a\" b")]
    [Arguments("a $@\"a {a} a\" a", "b $@\"a {b} a\" b")]
    [Arguments("a @$\"a {a} a\" a", "b @$\"a {b} a\" b")]
    [Arguments("a $@\"a \"\"a\"\" {a}\" a", "b $@\"a \"\"a\"\" {b}\" b")]
    [Arguments(
        "a $\"{{a}} {a + new { Value = a }} a\" a",
        "b $\"{{a}} {b + new { Vblue = b }} a\" b"
    )]
    [Arguments("$\"{a ? \"a\" : a} a\" a", "$\"{b ? \"a\" : b} a\" b")]
    [Arguments("a \"\"\"a\"\"\" a", "b \"\"\"a\"\"\" b")]
    [Arguments("a \"\"\"\"a \"\"\" a\"\"\"\" a", "b \"\"\"\"a \"\"\" a\"\"\"\" b")]
    [Arguments("a \"a\na", "b \"a\nb")]
    [Arguments("a // a\r\na", "b // a\r\nb")]
    [Arguments("a /* a", "b /* a")]
    [Arguments("a \"\"\"a", "b \"\"\"a")]
    [Arguments("a / a", "b / b")]
    [Arguments("aaaaa", "bbbbb")]
    public void CodeRunsKeepCommentsLiteralsAndInterpolationBoundaries(
        string source,
        string expected
    )
    {
        SparseCodeRewrite
            .ReplaceOutsideLiteralsAndComments(source, "a", "b", CancellationToken.None)
            .ShouldBe(expected);
    }

    [Test]
    public void SparseMatchesDoNotSkipAProtectedRegionBetweenCandidates()
    {
        var prefix = new string('x', 10000);
        var source = prefix + "needle /* needle */ " + prefix + "\"needle\" needle";
        var expected = prefix + "found /* needle */ " + prefix + "\"needle\" found";
        SparseCodeRewrite
            .ReplaceOutsideLiteralsAndComments(source, "needle", "found", CancellationToken.None)
            .ShouldBe(expected);
    }
}
