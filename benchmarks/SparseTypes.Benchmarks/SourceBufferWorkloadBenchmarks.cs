using BenchmarkDotNet.Attributes;
using SparseTypes.Generator.Shared;

[MemoryDiagnoser]
public class SourceBufferWorkloadBenchmarks
{
    private const string OldValue = "global::Ns.Child.Observable";
    private const string NewValue = "global::Impl.ChildImpl.Observable";
    private string _largeSource = null!;
    private string _ordinarySource = null!;

    [Params(4096, 8192)]
    public int LargeRepetitions { get; set; }

    [Params(0, 4, 16)]
    public int OrdinaryGroups { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _largeSource = string.Concat(
            Enumerable.Repeat("var view = " + OldValue + ".Create();\n", LargeRepetitions)
        );
        _ordinarySource = string.Concat(Enumerable.Repeat("    // generated member body\n", 1024));
        var expected = _largeSource.Replace(OldValue, NewValue, StringComparison.Ordinal);
        if (RewriteLarge() != expected)
            throw new InvalidOperationException("The large rewrite must preserve every reference.");
        if (EmitOrdinaryGroup() != 4 * _ordinarySource.Length)
            throw new InvalidOperationException(
                "All simultaneous source owners must remain intact."
            );
        if (RewriteThenEmit() != expected.Length + OrdinaryGroups * 4 * _ordinarySource.Length)
            throw new InvalidOperationException(
                "Mixed source workloads must preserve output sizes."
            );
    }

    [Benchmark]
    public int RewriteThenEmit()
    {
        var length = RewriteLarge().Length;
        for (var group = 0; group < OrdinaryGroups; group++)
            length += EmitOrdinaryGroup();
        return length;
    }

    private string RewriteLarge() =>
        SparseCodeRewrite.ReplaceOutsideLiteralsAndComments(
            _largeSource,
            OldValue,
            NewValue,
            CancellationToken.None
        );

    private int EmitOrdinaryGroup()
    {
        using var first = new SharedIndentedBuilder(CancellationToken.None);
        using var second = new SharedIndentedBuilder(CancellationToken.None);
        using var third = new SharedIndentedBuilder(CancellationToken.None);
        using var fourth = new SharedIndentedBuilder(CancellationToken.None);
        first.Append(_ordinarySource);
        second.Append(_ordinarySource);
        third.Append(_ordinarySource);
        fourth.Append(_ordinarySource);
        var a = first.ToString();
        var b = second.ToString();
        var c = third.ToString();
        var d = fourth.ToString();
        if (
            a != _ordinarySource
            || b != _ordinarySource
            || c != _ordinarySource
            || d != _ordinarySource
        )
            throw new InvalidOperationException(
                "Borrowed source buffers must have exclusive owners."
            );
        return a.Length + b.Length + c.Length + d.Length;
    }
}
