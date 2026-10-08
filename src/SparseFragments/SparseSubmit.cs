using System.Collections.Generic;
using System.ComponentModel;

namespace SparseFragments;

/// <summary>The outcome of a server submit or rebase.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public enum SparseSubmitStatus
{
    /// <summary>The submitted state was accepted.</summary>
    Accepted,

    /// <summary>Local edits were reapplied onto an authoritative server state.</summary>
    Rebased,

    /// <summary>Some local edits conflict with the authoritative server state.</summary>
    Conflicted,

    /// <summary>The server rejected the change without an authoritative state.</summary>
    Rejected,

    /// <summary>The submit failed without changing the session baseline.</summary>
    Failed,
}

/// <summary>A response returned by a submit delegate.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseSubmitResponse<TModel>
    where TModel : class
{
    private SparseSubmitResponse(SparseSubmitStatus status, TModel? serverState)
    {
        Status = status;
        ServerState = serverState;
    }

    /// <summary>The server response kind.</summary>
    public SparseSubmitStatus Status { get; }

    /// <summary>The authoritative server state, when supplied.</summary>
    public TModel? ServerState { get; }

    /// <summary>Creates an accepted response, optionally with authoritative server state.</summary>
    public static SparseSubmitResponse<TModel> Accepted(TModel? serverModel = null) =>
        new(SparseSubmitStatus.Accepted, serverModel);

    /// <summary>Creates a rejected response, optionally with the server's current state.</summary>
    public static SparseSubmitResponse<TModel> Rejected(TModel? serverCurrent = null) =>
        new(SparseSubmitStatus.Rejected, serverCurrent);

    /// <summary>Creates a failed response.</summary>
    public static SparseSubmitResponse<TModel> Failed() => new(SparseSubmitStatus.Failed, null);
}

/// <summary>The immutable snapshot and change set captured when a submit begins.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparsePendingSubmit<TChangeSet, TFragment>
    where TChangeSet : class
    where TFragment : class
{
    internal SparsePendingSubmit(TChangeSet changeSet, TFragment snapshot, long generation)
    {
        ChangeSet = changeSet;
        Snapshot = snapshot;
        Generation = generation;
    }

    /// <summary>The baseline-aware changes captured for transmission.</summary>
    public TChangeSet ChangeSet { get; }

    /// <summary>The model snapshot corresponding to the transmitted change set.</summary>
    public TFragment Snapshot { get; }

    /// <summary>The session generation at capture time.</summary>
    public long Generation { get; }
}

/// <summary>The outcome of completing a submit, including any rebase conflicts.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseSubmitResult
{
    internal SparseSubmitResult(
        SparseSubmitStatus status,
        IReadOnlyList<SparsePatchConflict>? conflicts = null
    )
    {
        Status = status;
        Conflicts = conflicts ?? System.Array.Empty<SparsePatchConflict>();
    }

    /// <summary>The submit outcome.</summary>
    public SparseSubmitStatus Status { get; }

    /// <summary>Conflicts reported while rebasing against server state.</summary>
    public IReadOnlyList<SparsePatchConflict> Conflicts { get; }
}
