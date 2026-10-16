using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SparseTypes;

/// <summary>Controls what unregistering or replacing a changed session does.</summary>
public enum EditSessionGroupRemovalPolicy
{
    /// <summary>Reject removal while the session has changes.</summary>
    RequireClean,

    /// <summary>Remove the registration while leaving its session untouched.</summary>
    DetachWithPendingChanges,
}

/// <summary>Operations and readiness checks for one typed session registration.</summary>
/// <remarks>
/// A registered session should be acknowledged and reconciled through its
/// <see cref="EditSessionGroup"/>. Generated sessions report baseline revisions
/// so the group can detect direct baseline changes.
/// </remarks>
/// <typeparam name="TModel">The editable model type.</typeparam>
/// <typeparam name="TChangeSet">The generated immutable change-set type.</typeparam>
public sealed class EditSessionGroupRegistration<TModel, TChangeSet>
    where TModel : class
    where TChangeSet : class
{
    /// <summary>Creates an unconfigured registration.</summary>
    /// <remarks>Set the required session and change-set delegates before registering it.</remarks>
    public EditSessionGroupRegistration() { }

    /// <summary>Creates a registration with the standard generated session operations.</summary>
    /// <remarks>Capture uses the session's non-mutating change-set preview; acknowledgement merges and advances by the captured submission.</remarks>
    /// <param name="session">The generated session that captures and describes change sets.</param>
    public EditSessionGroupRegistration(ISparseEditSessionWithChangeSet<TModel, TChangeSet> session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Session = session;
        CreateChangeSet = session.CreateChangeSet;
        IsEmpty = session.IsEmpty;
        EnumerateChangedPaths = session.EnumerateChangedPaths;
    }

    /// <summary>Gets or initializes the edit session.</summary>
    public ISparseEditSession<TModel, TChangeSet> Session { get; init; } = null!;

    /// <summary>Captures the session's current change set without mutating the session.</summary>
    public Func<TChangeSet> CreateChangeSet { get; init; } = null!;

    /// <summary>Tests whether a captured change set is empty.</summary>
    public Func<TChangeSet, bool> IsEmpty { get; init; } = null!;

    /// <summary>Gets changed paths from the captured change set.</summary>
    public Func<TChangeSet, IReadOnlyList<SparsePath>> EnumerateChangedPaths { get; init; } = null!;

    /// <summary>Gets a live-session readiness error, or <see langword="null"/> when the section may be submitted.</summary>
    /// <remarks>This readiness hook may inspect the current mutable session, UI state, or permission to submit.</remarks>
    public Func<string?>? Validate { get; init; }

    /// <summary>Validates the captured change set, returning an error when it cannot be submitted.</summary>
    /// <remarks>Receives the exact immutable <typeparamref name="TChangeSet"/> placed in the eventual submission.</remarks>
    public Func<TChangeSet, string?>? ValidateChanges { get; init; }

    /// <summary>Asynchronously validates the captured change set, returning an error when it cannot be submitted.</summary>
    /// <remarks>Receives the exact immutable <typeparamref name="TChangeSet"/> placed in the eventual submission.</remarks>
    public Func<TChangeSet, CancellationToken, Task<string?>>? ValidateAsync { get; init; }

    /// <summary>Reconciles an acknowledged change set with an authoritative persisted model.</summary>
    /// <remarks>Use <see cref="EditSessionGroup.TryReconcile{TModel, TChangeSet}"/> while the session is registered.</remarks>
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
