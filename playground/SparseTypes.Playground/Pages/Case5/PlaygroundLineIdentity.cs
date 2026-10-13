using System;

namespace SparseTypes.Playground.Models;

/// <summary>Correlates saved order-line identities by temporary key, never by list position.</summary>
/// <remarks>
/// After persistence, database-generated identities live on the persisted
/// projection while the local draft still carries temporary identities (plus
/// edits made after submit). Matching runs on <c>TemporaryId</c>, so
/// persisted order, count differences, and concurrent membership changes
/// cannot misassign IDs. Callers restore temporary identities onto the
/// persisted projection first with
/// <c>SparseEntityMapper.ApplyWithResult</c> plus
/// <c>AssignTemporaryKeys</c>; this type only performs the final writeback
/// into the live draft.
/// </remarks>
public static class PlaygroundLineIdentity
{
    /// <summary>Writes persisted identities into the matching local draft lines.</summary>
    /// <param name="submitted">The submitted snapshot carrying temporary identities.</param>
    /// <param name="persisted">
    /// The post-save projection with generated identities and restored temporary
    /// identities (see <c>AssignTemporaryKeys</c>).
    /// </param>
    /// <param name="local">The live draft updated in place.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="submitted"/>, <paramref name="persisted"/>, or
    /// <paramref name="local"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// One temporary identity matches multiple persisted rows, or a correlated
    /// persisted row lacks a generated identity.
    /// </exception>
    public static void RetargetNewLineIds(
        PlaygroundOrderDto submitted,
        PlaygroundOrderDto persisted,
        PlaygroundOrderDto local
    )
    {
        ArgumentNullException.ThrowIfNull(submitted);
        ArgumentNullException.ThrowIfNull(persisted);
        ArgumentNullException.ThrowIfNull(local);

        // Build phase: no mutation, so unsupported shapes fail before
        // rewriting any part of the live draft.
        var persistedByTemp = new Dictionary<Guid, PlaygroundOrderLineDto>();
        foreach (var persistedLine in persisted.Lines)
        {
            if (persistedLine.TemporaryId is null)
            {
                continue;
            }

            var temp = persistedLine.TemporaryId.Value;
            if (persistedByTemp.ContainsKey(temp))
            {
                throw new InvalidOperationException(
                    $"Multiple persisted rows carry temporary identity '{temp}'. Correlation requires unique temporary identities per collection scope."
                );
            }

            persistedByTemp.Add(temp, persistedLine);
        }

        var updates = new List<(PlaygroundOrderLineDto Line, int Id)>();
        foreach (var submittedLine in submitted.Lines)
        {
            if (submittedLine.Id.HasValue || submittedLine.TemporaryId is null)
            {
                continue;
            }

            // Missing, deleted, or filtered rows stay untouched: only matched
            // rows are collected for writeback.
            if (
                !persistedByTemp.TryGetValue(submittedLine.TemporaryId.Value, out var persistedLine)
            )
            {
                continue;
            }

            var localLine = local.Lines.FirstOrDefault(line =>
                line.TemporaryId == submittedLine.TemporaryId
            );
            if (localLine is null)
            {
                continue;
            }

            if (!persistedLine.Id.HasValue)
            {
                throw new InvalidOperationException(
                    "A saved line did not receive a database identity."
                );
            }

            updates.Add((localLine, persistedLine.Id.Value));
        }

        foreach (var (line, id) in updates)
        {
            line.Id = id;
            line.TemporaryId = null;
        }
    }
}
