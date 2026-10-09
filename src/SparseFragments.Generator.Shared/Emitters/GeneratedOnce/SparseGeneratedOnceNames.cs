using System.Threading;

namespace SparseFragments.Generator.Shared;

/// <summary>Stable names for compilation-scoped Generated-Once helpers.</summary>
/// <remarks>
/// Centralizes the shared helper identities used by the track-3 migrations
/// (#181 read-only adapters, #182 clone kernels, #183 removal index) so
/// per-model emitters and the compilation-scoped emission plane agree without
/// hard-coding the owning generator's namespace. There is no implicit
/// fallback: callers pass the explicit namespace from
/// <see cref="SparseGeneratorConfig.GeneratedImplementationNamespace"/>.
/// Interim seam: until #178 lands its capability-driven emission plane, the
/// product generator aggregates requirements manually and emits these helpers
/// once per compilation; #178 should replace that manual wiring, not the
/// names defined here.
/// </remarks>
internal static class SparseGeneratedOnceNames
{
    /// <summary>Simple name of the shared read-only list adapter.</summary>
    public const string ReadOnlyListAdapter = "SparseReadOnlyListAdapter";

    /// <summary>Simple name of the shared read-only dictionary adapter.</summary>
    public const string ReadOnlyDictionaryAdapter = "SparseReadOnlyDictionaryAdapter";

    /// <summary>Simple name of the shared read-only dictionary-entries adapter.</summary>
    public const string ReadOnlyDictionaryEntriesAdapter = "SparseReadOnlyDictionaryEntriesAdapter";

    /// <summary>Simple name of the shared collection clone kernel container.</summary>
    public const string CloneKernels = "SparseCloneKernels";

    /// <summary>Simple name of the shared ordered removal-index helper.</summary>
    public const string RemovalIndex = "SparseRemovalIndex";

    /// <summary>Qualified name of the shared list adapter.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <returns>Globally qualified type name.</returns>
    public static string QualifiedListAdapter(string implementationNamespace) =>
        "global::" + implementationNamespace + "." + ReadOnlyListAdapter;

    /// <summary>Qualified name of the shared dictionary adapter.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <returns>Globally qualified type name.</returns>
    public static string QualifiedDictionaryAdapter(string implementationNamespace) =>
        "global::" + implementationNamespace + "." + ReadOnlyDictionaryAdapter;

    /// <summary>Qualified name of the shared dictionary-entries adapter.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <returns>Globally qualified type name.</returns>
    public static string QualifiedDictionaryEntriesAdapter(string implementationNamespace) =>
        "global::" + implementationNamespace + "." + ReadOnlyDictionaryEntriesAdapter;

    /// <summary>Qualified name of the shared clone kernel container.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <returns>Globally qualified type name.</returns>
    public static string QualifiedCloneKernels(string implementationNamespace) =>
        "global::" + implementationNamespace + "." + CloneKernels;

    /// <summary>Qualified name of the shared removal-index helper.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <returns>Globally qualified type name.</returns>
    public static string QualifiedRemovalIndex(string implementationNamespace) =>
        "global::" + implementationNamespace + "." + RemovalIndex;

    /// <summary>Stable hint name for the shared read-only adapter source.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <returns>Deterministic hint name.</returns>
    public static string ReadOnlyAdaptersHintName(string implementationNamespace) =>
        implementationNamespace + ".ReadOnlyAdapters.g.cs";

    /// <summary>Stable hint name for the shared clone kernel source.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <returns>Deterministic hint name.</returns>
    public static string CloneKernelsHintName(string implementationNamespace) =>
        implementationNamespace + ".CloneKernels.g.cs";

    /// <summary>Stable hint name for the shared removal-index source.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <returns>Deterministic hint name.</returns>
    public static string RemovalIndexHintName(string implementationNamespace) =>
        implementationNamespace + ".RemovalIndex.g.cs";

    /// <summary>Opens a block-scoped generated namespace compatible with C# 9.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public static void AppendGeneratedHeader(
        SharedIndentedBuilder code,
        string implementationNamespace,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        code.AppendLine("// <auto-generated />");
        code.AppendLine("#nullable enable");
        // Block-scoped namespace: file-scoped namespaces need C# 10, while the
        // generated surface targets C# 9 as its minimum language version.
        code.Append("namespace ").Append(implementationNamespace).AppendLine();
        code.AppendLine("{");
    }
}
