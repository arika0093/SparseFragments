using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace SparseFragments.Generator.Shared;

/// <summary>Identifies a promoted type whose roots disagree on generated semantics.</summary>
internal sealed record SparseIncompatiblePromotedEntry(string Key, string DisplayName)
{
    public bool Equals(SparseIncompatiblePromotedEntry? other) =>
        other is not null
        && string.Equals(Key, other.Key, StringComparison.Ordinal)
        && string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal);

    public override int GetHashCode() =>
        unchecked((Key.GetHashCode() * 31) + DisplayName.GetHashCode(StringComparison.Ordinal));
}

/// <summary>
/// Keyed aggregation result for promoted models: distinct emit candidates plus
/// incompatible keys. Sorted by fully-qualified type name so hint names and
/// incompatibility diagnostics stay deterministic regardless of root declaration order.
/// </summary>
internal sealed record SparsePromotedDedupResult(
    ImmutableArray<SparsePromotedModel> Distinct,
    ImmutableArray<SparseIncompatiblePromotedEntry> Incompatible
)
{
    public bool Equals(SparsePromotedDedupResult? other) =>
        other is not null
        && SparseSequence.Equal(Distinct, other.Distinct)
        && SparseSequence.Equal(Incompatible, other.Incompatible);

    public override int GetHashCode() =>
        unchecked((SparseSequence.Hash(Distinct) * 31) + SparseSequence.Hash(Incompatible));
}

/// <summary>
/// Keyed aggregation for promoted models. Deduplicates per promoted type key so
/// downstream per-model emit stages stay cached when unrelated roots change.
/// </summary>
internal static class SparsePromotedAggregation
{
    /// <summary>
    /// Collects promoted contributions from analyzed roots and resolves them into
    /// distinct emit candidates plus incompatible keys in one product-neutral step.
    /// </summary>
    /// <remarks>
    /// Source rendering remains generator-specific; this helper owns only the
    /// collect/dedupe/incompatibility-resolution mechanics so downstream
    /// generators compose with the shared model instead of duplicating it.
    /// Explicit roots (types that are themselves fragment models) never count as
    /// promoted contributions.
    /// </remarks>
    internal static SparsePromotedDedupResult Aggregate(
        ImmutableArray<SparseGenerationAnalysis> analyses,
        CancellationToken cancellationToken
    )
    {
        var contributions = ImmutableArray.CreateBuilder<SparsePromotedModel>();
        var explicitRoots = ImmutableArray.CreateBuilder<string>();
        foreach (var analysis in analyses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (analysis.Model is { } model && model.ModelTypeName is not null)
            {
                explicitRoots.Add(model.ModelTypeName);
            }

            if (!analysis.PromotedModels.IsDefault)
            {
                contributions.AddRange(analysis.PromotedModels);
            }
        }

        return Deduplicate(
            contributions.ToImmutable(),
            explicitRoots.ToImmutable(),
            cancellationToken
        );
    }

    internal static ImmutableArray<string> ToIncompatibleArguments(
        SparsePromotedDedupResult result
    ) => result.Incompatible.Select(static entry => entry.DisplayName).ToImmutableArray();

    internal static SparsePromotedDedupResult Deduplicate(
        ImmutableArray<SparsePromotedModel> contributions,
        ImmutableArray<string> explicitRootNames,
        CancellationToken cancellationToken
    )
    {
        var explicitRoots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in explicitRootNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (name is not null)
            {
                explicitRoots.Add(name);
            }
        }

        var deduped = new SortedDictionary<string, SparsePromotedModel>(StringComparer.Ordinal);
        var displayNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var incompatible = new HashSet<string>(StringComparer.Ordinal);
        foreach (var promoted in contributions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = promoted.Model.ModelTypeName;
            if (explicitRoots.Contains(key))
            {
                continue;
            }

            if (!displayNames.ContainsKey(key))
            {
                displayNames[key] = promoted.Model.Name;
            }

            if (deduped.TryGetValue(key, out var existing))
            {
                if (!existing.Equals(promoted))
                {
                    incompatible.Add(key);
                }

                continue;
            }

            deduped.Add(key, promoted);
        }

        var distinct = ImmutableArray.CreateBuilder<SparsePromotedModel>();
        foreach (var pair in deduped)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!incompatible.Contains(pair.Key))
            {
                distinct.Add(pair.Value);
            }
        }

        var conflicts = ImmutableArray.CreateBuilder<SparseIncompatiblePromotedEntry>();
        foreach (var key in incompatible.OrderBy(static value => value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            conflicts.Add(new SparseIncompatiblePromotedEntry(key, displayNames[key]));
        }

        return new SparsePromotedDedupResult(distinct.ToImmutable(), conflicts.ToImmutable());
    }
}
