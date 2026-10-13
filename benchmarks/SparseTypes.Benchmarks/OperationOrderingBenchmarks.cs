using System.Text;
using BenchmarkDotNet.Attributes;
using SparseTypes.Generator.Shared;

[MemoryDiagnoser]
public class OperationOrderingBenchmarks
{
    [Params(16, 512)]
    public int MembersPerGroup { get; set; }

    [Params(false, true)]
    public bool AlreadyOrdered { get; set; }

    private string _source = null!;

    [GlobalSetup]
    public void Setup()
    {
        var source = new StringBuilder();
        var expected = new StringBuilder();
        for (var rank = 0; rank < 3; rank++)
        for (var index = 0; index < MembersPerGroup; index++)
            AppendMember(expected, rank, index);
        if (AlreadyOrdered)
            source.Append(expected);
        else
            for (var index = 0; index < MembersPerGroup; index++)
            for (var rank = 2; rank >= 0; rank--)
            {
                source.AppendLine();
                AppendMember(source, rank, index);
            }
        _source = source.ToString();
        if (SparseOperationOrdering.ReorderInternalFirst(_source) != expected.ToString())
            throw new InvalidOperationException(
                "Ordering must preserve bodies and relative member order."
            );
        if (AppendReordered() != expected.ToString() || AppendOrdered() != expected.ToString())
            throw new InvalidOperationException(
                "Streaming and buffered ordering must emit identical text."
            );
    }

    private static void AppendMember(StringBuilder source, int rank, int index)
    {
        if (rank == 1)
        {
            source.AppendLine("        /// <summary>Operation entry point.</summary>");
            source.AppendLine("        /// <returns>The member index.</returns>");
        }
        var access = rank switch
        {
            0 => "public",
            1 => "internal",
            _ => "private",
        };
        source
            .Append("        ")
            .Append(access)
            .Append(" static int Method")
            .Append(rank)
            .Append('_')
            .Append(index)
            .AppendLine("()");
        source.AppendLine("        {");
        source.AppendLine(
            "            // public and private tokens in bodies keep their positions."
        );
        source.Append("            return ").Append(index).AppendLine(";");
        source.AppendLine("        }");
    }

    [Benchmark]
    public string Reorder() => SparseOperationOrdering.ReorderInternalFirst(_source);

    [Benchmark]
    public string AppendReordered()
    {
        var code = new SharedIndentedBuilder(CancellationToken.None);
        code.Append(SparseOperationOrdering.ReorderInternalFirst(_source));
        return code.ToString();
    }

    [Benchmark]
    public string AppendOrdered()
    {
        var code = new SharedIndentedBuilder(CancellationToken.None);
        SparseOperationOrdering.AppendInternalFirst(code, _source);
        return code.ToString();
    }
}
