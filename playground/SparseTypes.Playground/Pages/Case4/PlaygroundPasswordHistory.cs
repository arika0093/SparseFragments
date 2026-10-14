using SparseTypes;
using SparseTypes.Playground.Models;

namespace SparseTypes.Playground.Pages.Case4;

/// <summary>Creates the Case 4 edit history without retaining password changes.</summary>
public static class PlaygroundPasswordHistory
{
    /// <summary>Creates a history that replays every changed member except <see cref="PlaygroundSettings.Password"/>.</summary>
    /// <param name="session">The session whose transitions are observed.</param>
    /// <param name="model">The live model changed by the session.</param>
    /// <returns>A caller-owned edit history.</returns>
    public static SparseEditHistory<PlaygroundSettings.ChangeSet> Create(
        PlaygroundSettings.EditSession session,
        PlaygroundSettings model
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(model);

        return new SparseEditHistory<PlaygroundSettings.ChangeSet>(
            new EditHistoryOptions(),
            new SparseEditHistoryHooks<PlaygroundSettings.ChangeSet>
            {
                IsEmpty = changes => changes.IsEmpty || IsPasswordOnlyChange(changes),
                Invert = static changes => changes.Invert(),
                Compose = static (first, next) => first.Compose(next),
                TryApply = (
                    PlaygroundSettings.ChangeSet changes,
                    out IReadOnlyList<SparseConflict>? conflicts
                ) => TryApplyWithoutPassword(session, model, changes, out conflicts),
                Subscribe = handler => session.TransitionObserved += handler,
                Unsubscribe = handler => session.TransitionObserved -= handler,
            }
        );
    }

    private static bool IsPasswordOnlyChange(PlaygroundSettings.ChangeSet changes)
    {
        var paths = changes.EnumerateChangedPaths();
        return paths.Count != 0
            && paths.All(path =>
                string.Equals(
                    path.ToString(),
                    nameof(PlaygroundSettings.Password),
                    StringComparison.Ordinal
                )
            );
    }

    private static bool TryApplyWithoutPassword(
        PlaygroundSettings.EditSession session,
        PlaygroundSettings model,
        PlaygroundSettings.ChangeSet changes,
        out IReadOnlyList<SparseConflict>? conflicts
    )
    {
        var patch = changes.ToPatch();
        patch.Password.Keep();
        var current = PlaygroundSettings.Fragment.From(model);
        var updated = patch.Apply(Optional<PlaygroundSettings.Fragment?>.Present(current)).Value;
        if (updated is null)
        {
            throw new InvalidOperationException("Undo/redo could not produce a settings fragment.");
        }

        var filtered = PlaygroundSettings.ChangeSet.Between(model, updated.ToModel());
        if (filtered.IsEmpty)
        {
            conflicts = null;
            return true;
        }

        return session.TryApplyInPlace(filtered, out conflicts);
    }
}
