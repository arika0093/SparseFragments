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
    [Arguments("a/* a */ a", "a/*", "b", "b a */ a")]
    [Arguments("a\"tail\" a", "a\"", "b", "btail\" a")]
    [Arguments("// a\na", "\na", "b", "// ab")]
    [Arguments("aaaaa", "aa", "b", "bba")]
    [Arguments("a a", "a", "\"", "\" \"")]
    public void MatchesKeepTheirOriginalAnchorsAndDoNotRescanReplacementText(
        string source,
        string oldValue,
        string newValue,
        string expected
    )
    {
        SparseCodeRewrite
            .ReplaceOutsideLiteralsAndComments(source, oldValue, newValue, CancellationToken.None)
            .ShouldBe(expected);
    }

    [Test]
    public void ProtectedMatchesReturnTheOriginalSourceInstance()
    {
        const string source = "\"needle\" // needle\n/* needle */";
        SparseCodeRewrite
            .ReplaceOutsideLiteralsAndComments(source, "needle", "found", CancellationToken.None)
            .ShouldBeSameAs(source);
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
