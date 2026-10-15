using SparseTypes.Playground.Models;

namespace SparseTypes.Playground.Pages.Case5;

internal sealed class PlaygroundSaveSnapshot
{
    private PlaygroundSaveSnapshot(
        PlaygroundOrderDto.ChangeSet submitted,
        PlaygroundOrderDto submittedDraft,
        int baseRevision
    )
    {
        Submitted = submitted;
        SubmittedDraft = submittedDraft;
        BaseRevision = baseRevision;
    }

    internal PlaygroundOrderDto.ChangeSet Submitted { get; }

    internal PlaygroundOrderDto SubmittedDraft { get; }

    internal int BaseRevision { get; }

    internal static PlaygroundSaveSnapshot Capture(
        PlaygroundOrderDto.EditSession session,
        PlaygroundOrderDto liveDraft,
        int baseRevision
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(liveDraft);
        return new PlaygroundSaveSnapshot(
            session.CreateChangeSet(),
            liveDraft.DeepClone(),
            baseRevision
        );
    }
}
