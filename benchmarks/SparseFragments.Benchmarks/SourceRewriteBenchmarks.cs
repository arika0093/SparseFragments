using System.Text;
using BenchmarkDotNet.Attributes;
using SparseFragments.Generator.Shared;

public enum SourceRewriteShape
{
    Code,
    Mixed,
    LiteralOnly,
    Absent,
}

[MemoryDiagnoser]
public class SourceRewriteBenchmarks
{
    private const string OldValue = "global::Ns.Child.Observable";
    private const string NewValue = "global::Impl.ChildImpl.Observable";

    [Params(16, 512)]
    public int Repetitions { get; set; }

    [ParamsAllValues]
    public SourceRewriteShape Shape { get; set; }

    private string _source = null!;

    [GlobalSetup]
    public void Setup()
    {
        const string mixed = """"
            var view = __CODE_REFERENCE__.Create(); // __LITERAL_REFERENCE__
            var text = "__LITERAL_REFERENCE__";
            var escaped = "\"__LITERAL_REFERENCE__";
            var verbatim = @"__LITERAL_REFERENCE__ ""quoted""";
            var interpolated = $"__LITERAL_REFERENCE__ {__CODE_REFERENCE__.Create()}";
            var interpolatedVerbatim = $@"__LITERAL_REFERENCE__ {__CODE_REFERENCE__.Create()}";
            var raw = """__LITERAL_REFERENCE__""";
            /* __LITERAL_REFERENCE__ */
            #region __LITERAL_REFERENCE__
            #endregion
            """";
        var template = Shape switch
        {
            SourceRewriteShape.Code => "var view = __CODE_REFERENCE__.Create();",
            SourceRewriteShape.Mixed => mixed,
            SourceRewriteShape.LiteralOnly => mixed.Replace(
                "__CODE_REFERENCE__",
                "OtherType",
                StringComparison.Ordinal
            ),
            _ => "var view = OtherType.Create();",
        };
        var source = new StringBuilder();
        var expected = new StringBuilder();
        for (var index = 0; index < Repetitions; index++)
        {
            source.AppendLine(
                template
                    .Replace("__CODE_REFERENCE__", OldValue, StringComparison.Ordinal)
                    .Replace("__LITERAL_REFERENCE__", OldValue, StringComparison.Ordinal)
            );
            expected.AppendLine(
                template
                    .Replace("__CODE_REFERENCE__", NewValue, StringComparison.Ordinal)
                    .Replace("__LITERAL_REFERENCE__", OldValue, StringComparison.Ordinal)
            );
        }
        _source = source.ToString();
        AssertRewrite(_source, OldValue, NewValue, expected.ToString());
        ValidateBoundaries();
    }

    private static void ValidateBoundaries()
    {
        AssertRewrite("aaaaa", "aa", "b", "bba");
        AssertRewrite("aaaaa", "aa", "", "a");
        AssertRewrite("a", "a", "aa", "aa");
        AssertRewrite("unchanged", "", "new", "unchanged");
        AssertRewrite("", "a", "b", "");
        AssertRewrite("\"aaaa\" aaaa /* aaaa */", "aa", "b", "\"aaaa\" bb /* aaaa */");
        AssertRewrite("'a' a", "a", "b", "'a' b");
        AssertRewrite("// a\na", "a", "b", "// a\nb");
        AssertRewrite("#region a\na", "a", "b", "#region a\nb");
        AssertRewrite("\"a\"a", "\"a", "b", "\"a\"a");
        AssertRewrite("$\"text {a + (a)}\"", "a", "b", "$\"text {b + (b)}\"");
        AssertRewrite("$\"{{a}} {a}\"", "a", "b", "$\"{{a}} {b}\"");
        AssertRewrite("\"\"\"a\"\"\" a", "a", "b", "\"\"\"a\"\"\" b");
        AssertRewrite("a\"x\" a", "a\"", "b", "bx\" a");
        AssertRewrite("a/* a */ a", "a/*", "b", "b a */ a");
        AssertRewrite("\"aaa\"aa", "aa", "b", "\"aaa\"b");
        AssertRewrite("aa'aa'aa", "aa", "\"", "\"'aa'\"");
        AssertRewrite("$\"{a}\"", "{", "X", "$\"Xa}\"");
        AssertRewrite("$\"{\"a\" + a}\"", "a", "b", "$\"{\"a\" + b}\"");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            SparseCodeRewrite.ReplaceOutsideLiteralsAndComments(
                OldValue,
                OldValue,
                NewValue,
                cancellation.Token
            );
        }
        catch (OperationCanceledException)
        {
            return;
        }
        throw new InvalidOperationException("Source rewriting must observe cancellation.");
    }

    private static void AssertRewrite(
        string source,
        string oldValue,
        string newValue,
        string expected
    )
    {
        var actual = SparseCodeRewrite.ReplaceOutsideLiteralsAndComments(
            source,
            oldValue,
            newValue,
            CancellationToken.None
        );
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Source rewriting changed code or protected text.");
        }
    }

    [Benchmark]
    public string Rewrite() =>
        SparseCodeRewrite.ReplaceOutsideLiteralsAndComments(
            _source,
            OldValue,
            NewValue,
            CancellationToken.None
        );
}
