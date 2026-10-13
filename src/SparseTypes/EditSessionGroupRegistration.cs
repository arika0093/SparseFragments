using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SparseTypes;

/// <summary>Controls what unregistering or replacing a changed session does.</summary>
public enum EditSessionGroupRemovalPolicy
{
    /// <summary>Reject removal while the session has changes or open drafts.</summary>
    RequireClean,

    /// <summary>Remove the registration while leaving its session and drafts untouched.</summary>
    DetachWithPendingChanges,
}

/// <summary>Operations and readiness checks for one typed session registration.</summary>
/// <typeparam name="TModel">The editable model type.</typeparam>
/// <typeparam name="TChangeSet">The generated immutable change-set type.</typeparam>
public sealed class EditSessionGroupRegistration<TModel, TChangeSet>
    where TModel : class
    where TChangeSet : class
{
    /// <summary>Gets or initializes the edit session.</summary>
    public ISparseEditSession<TModel, TChangeSet> Session { get; init; } = null!;

    /// <summary>Captures the session's current change set.</summary>
    public Func<TChangeSet> CreateChangeSet { get; init; } = null!;

    /// <summary>Tests whether a captured change set is empty.</summary>
    public Func<TChangeSet, bool> IsEmpty { get; init; } = null!;

    /// <summary>Gets changed paths from the captured change set.</summary>
    public Func<TChangeSet, IReadOnlyList<SparsePath>> EnumerateChangedPaths { get; init; } = null!;

    /// <summary>
    /// Gets a live-session readiness error, or <see langword="null"/> when the section may be submitted.
    /// </summary>
    /// <remarks>
    /// This readiness hook may inspect the current mutable session, open drafts, UI state,
    /// or permission to submit. It does not receive the captured submission and must not
    /// be used as validation of an earlier immutable change set. Use
    /// <see cref="ValidateChanges"/> or <see cref="ValidateAsync"/> to validate the captured data.
    /// </remarks>
    public Func<string?>? Validate { get; init; }

    /// <summary>
    /// Validates the captured change set, returning an error when it cannot be submitted.
    /// </summary>
    /// <remarks>
    /// Receives the exact immutable <typeparamref name="TChangeSet"/> placed in the eventual
    /// submission. It must not inspect the live session; post-capture edits remain pending
    /// and do not change the value observed here.
    /// </remarks>
    public Func<TChangeSet, string?>? ValidateChanges { get; init; }

    /// <summary>
    /// Asynchronously validates the captured change set, returning an error when it cannot be submitted.
    /// </summary>
    /// <remarks>
    /// Receives the exact immutable <typeparamref name="TChangeSet"/> placed in the eventual
    /// submission. Edits made while validation awaits do not change the value observed here.
    /// </remarks>
    public Func<TChangeSet, CancellationToken, Task<string?>>? ValidateAsync { get; init; }

    /// <summary>
    /// Reports open drafts. If omitted, the session must implement
    /// <see cref="ISparseEditSessionOpenDraftState"/>.
    /// </summary>
    public Func<bool>? HasOpenDrafts { get; init; }

    /// <summary>
    /// Reconciles an acknowledged change set with an authoritative persisted model.
    /// Return <see langword="false"/> and an error when reconciliation cannot be applied.
    /// </summary>
    public Func<TChangeSet, TModel, (bool Success, string? Error)>? TryReconcile { get; init; }
}

/// <summary>A typed handle for one stable section registration in an edit-session group.</summary>
/// <typeparam name="TModel">The editable model type.</typeparam>
/// <typeparam name="TChangeSet">The generated immutable change-set type.</typeparam>
public sealed class EditSessionSectionHandle<TModel, TChangeSet>
    where TModel : class
    where TChangeSet : class
{
    internal EditSessionSectionHandle(EditSessionGroup owner, IEditSessionGroupSection section)
    {
        Owner = owner;
        Section = section;
    }

    /// <summary>Gets the stable section identifier.</summary>
    public string SectionId => Section.SectionId;

    /// <summary>Gets the registered typed edit session.</summary>
    public ISparseEditSession<TModel, TChangeSet> Session =>
        (ISparseEditSession<TModel, TChangeSet>)Section.Session;

    internal EditSessionGroup Owner { get; }

    internal IEditSessionGroupSection Section { get; }
}
