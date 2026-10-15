using System.Threading;

namespace SparseTypes.Generator.Shared;

/// <summary>Compilation-scoped Generated-Once emission plane.</summary>
/// <remarks>Helpers here are model-independent, emitted once per consumer compilation, and normally <c>internal</c>.</remarks>
internal static class SparseGeneratedOnceEmitter
{
    /// <summary>Simple name of the shared clone kernel container.</summary>
    public const string CloneKernelsTypeName = SparseGeneratedOnceNames.CloneKernels;

    /// <summary>Simple name of the shared read-only list adapter.</summary>
    public const string ReadOnlyListAdapterTypeName = SparseGeneratedOnceNames.ReadOnlyListAdapter;

    /// <summary>Simple name of the shared read-only dictionary adapter.</summary>
    public const string ReadOnlyDictionaryAdapterTypeName =
        SparseGeneratedOnceNames.ReadOnlyDictionaryAdapter;

    /// <summary>Simple name of the shared read-only dictionary entries adapter.</summary>
    public const string ReadOnlyDictionaryEntriesAdapterTypeName =
        SparseGeneratedOnceNames.ReadOnlyDictionaryEntriesAdapter;

    /// <summary>Simple name of the shared removal-index helper.</summary>
    public const string RemovalIndexTypeName = SparseGeneratedOnceNames.RemovalIndex;

    /// <summary>Gets the stable hint name for the clone kernel source.</summary>
    /// <param name="implementationNamespace">Owning generator implementation namespace.</param>
    /// <returns>A stable unique hint name.</returns>
    public static string CloneKernelsHintName(string implementationNamespace) =>
        SparseGeneratedOnceNames.CloneKernelsHintName(implementationNamespace);

    /// <summary>Gets the stable hint name for the read-only adapter source.</summary>
    /// <param name="implementationNamespace">Owning generator implementation namespace.</param>
    /// <returns>A stable unique hint name.</returns>
    public static string ReadOnlyAdaptersHintName(string implementationNamespace) =>
        SparseGeneratedOnceNames.ReadOnlyAdaptersHintName(implementationNamespace);

    /// <summary>Gets the stable hint name for the removal-index source.</summary>
    /// <param name="implementationNamespace">Owning generator implementation namespace.</param>
    /// <returns>A stable unique hint name.</returns>
    public static string RemovalIndexHintName(string implementationNamespace) =>
        SparseGeneratedOnceNames.RemovalIndexHintName(implementationNamespace);

    /// <summary>Resolves the implementation namespace, or null when disabled.</summary>
    /// <param name="config">Owning generator configuration.</param>
    /// <returns>The explicit namespace, or null for no Generated-Once output.</returns>
    public static string? TryGetNamespace(SparseGeneratorConfig config) =>
        SparseGeneratedPlacement.TryGetImplementationNamespace(config);

    /// <summary>Renders the shared clone kernel source.</summary>
    /// <remarks>Per-model clone code already calls these kernels through the qualified container name.</remarks>
    /// <param name="implementationNamespace">Owning generator implementation namespace.</param>
    /// <param name="includePortableSetView">Target-framework set-view variant.</param>
    /// <param name="hashSetSupportsCapacity">Target-framework capacity support.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Model-independent helper source.</returns>
    public static string RenderCloneKernels(
        string implementationNamespace,
        bool includePortableSetView,
        bool hashSetSupportsCapacity,
        CancellationToken cancellationToken
    ) =>
        SparseGeneratedOnceCloneKernels.BuildSource(
            implementationNamespace,
            includePortableSetView,
            hashSetSupportsCapacity,
            cancellationToken
        );

    /// <summary>Renders the shared read-only adapter source.</summary>
    /// <remarks>The capability plane tracks adapters as one coarse family.</remarks>
    /// <param name="implementationNamespace">Owning generator implementation namespace.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Model-independent adapter source using only BCL types.</returns>
    public static string RenderReadOnlyAdapters(
        string implementationNamespace,
        CancellationToken cancellationToken
    ) =>
        SparseGeneratedOnceReadOnlyAdapters.BuildSource(
            implementationNamespace,
            includeCollection: true,
            includeDictionary: true,
            includeEntries: true,
            cancellationToken
        );

    /// <summary>Renders the shared removal-index helper source.</summary>
    /// <remarks>Shares removal-index kernels across keyed and dictionary patches.</remarks>
    /// <param name="implementationNamespace">Owning generator implementation namespace.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Generic index helpers over caller-owned removal lists.</returns>
    public static string RenderRemovalIndex(
        string implementationNamespace,
        CancellationToken cancellationToken
    ) => SparseGeneratedOnceRemovalIndex.BuildSource(implementationNamespace, cancellationToken);
}
