using BenchmarkDotNet.Attributes;
using SparseFragments.Generator.Shared;

[MemoryDiagnoser]
public class EditSessionTemplateBenchmarks
{
    [Params(false, true)]
    public bool AlternateDialect { get; set; }

    private SparseEditSessionDialect _session = null!;
    private SparseRuntimeDialect _runtime = null!;
    private SparseFragmentPatchEmitter.SparsePatchDialect _patch;
    private int _rebaseCalls;

    [GlobalSetup]
    public void Setup()
    {
        var product = AlternateDialect ? "TemplateBench.Beta" : "TemplateBench.Alpha";
        Configure(product);
        var first = SparseEditSessionEmitter.RenderCoreSources(_session, _runtime, _patch);
        var second = SparseEditSessionEmitter.RenderCoreSources(_session, _runtime, _patch);
        ValidateSources(first, product);
        if (first != second || _rebaseCalls != 4)
        {
            throw new InvalidOperationException(
                "Repeated rendering must preserve output and evaluate dialect delegates each time."
            );
        }
        var other = AlternateDialect ? "TemplateBench.Alpha" : "TemplateBench.Beta";
        Configure(other);
        var alternate = SparseEditSessionEmitter.RenderCoreSources(_session, _runtime, _patch);
        ValidateSources(alternate, other);
        if (alternate == first || _rebaseCalls != 2)
        {
            throw new InvalidOperationException(
                "Rendering must keep independent dialects separate."
            );
        }
        Configure(product);
        if (Render() != first.Core.Length + first.CurrentCore.Length || _rebaseCalls != 2)
        {
            throw new InvalidOperationException(
                "Restoring a dialect must retain the original output."
            );
        }
    }

    private static void ValidateSources((string Core, string CurrentCore) sources, string product)
    {
        var text = sources.Core + sources.CurrentCore;
        if (
            !sources.Core.Contains("namespace " + product + ".Generated", StringComparison.Ordinal)
            || !sources.CurrentCore.Contains(
                "namespace " + product + ".Generated",
                StringComparison.Ordinal
            )
            || !text.Contains("global::" + product + ".Optional", StringComparison.Ordinal)
            || !text.Contains("global::" + product + ".Conflict", StringComparison.Ordinal)
            || !text.Contains(
                "global::" + product + ".Rebase<TChangeSet>",
                StringComparison.Ordinal
            )
        )
        {
            throw new InvalidOperationException("Rendering must apply each explicit dialect.");
        }
    }

    private void Configure(string product)
    {
        _rebaseCalls = 0;
        var prefix = "global::" + product + ".";
        _session = new SparseEditSessionDialect(product + ".Generated");
        _runtime = new SparseRuntimeDialect(
            prefix,
            prefix + "Optional",
            prefix + "MergeStrategy",
            prefix + "Runtime",
            prefix + "Runtime",
            prefix + "Runtime",
            prefix + "Runtime",
            "__template_bench_merge_"
        );
        _patch = new SparseFragmentPatchEmitter.SparsePatchDialect(
            prefix,
            "__sparse_whole",
            "__SparseMembersEmpty",
            static member => "__sparse_patch_member_" + member.Id,
            static _ => string.Empty,
            "Apply",
            false,
            prefix + "Runtime",
            prefix + "Conflict",
            prefix + "ConflictKind",
            payload =>
            {
                _rebaseCalls++;
                return prefix + "Rebase<" + payload + ">";
            },
            static _ => "Patch",
            static _ => "ChangeSet",
            PayloadImplementationContainerPrefix: "TemplateBenchInternal"
        );
    }

    [Benchmark]
    public int Render()
    {
        var sources = SparseEditSessionEmitter.RenderCoreSources(_session, _runtime, _patch);
        return sources.Core.Length + sources.CurrentCore.Length;
    }
}
