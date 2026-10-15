namespace SparseTypes.Generator.Shared;

internal static class SparseEditSessionChildEmitter
{
    internal static string ChildRegistrationTuple(
        string pathType,
        string modelType,
        string conflictType
    ) =>
        "("
        + pathType
        + " Path, global::System.Func<bool> HasChanges, global::System.Func<(bool Accepted, global::System.Collections.Generic.IReadOnlyList<"
        + conflictType
        + ">? Conflicts)> TryPreflight, global::System.Func<(bool Accepted, global::System.Collections.Generic.IReadOnlyList<"
        + conflictType
        + ">? Conflicts)> TryAccept, global::System.Func<"
        + modelType
        + ", (bool Accepted, "
        + modelType
        + "? Updated, global::System.Collections.Generic.IReadOnlyList<"
        + conflictType
        + ">? Conflicts)> TryApply, global::System.Action Discard, global::System.Action Confirm, global::System.Func<bool> IsTargetMissing)";

    internal static void AppendChildOverlapGuard(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect
    )
    {
        if (patchDialect.ChildPathOverlapExpression is not { } childPathOverlap)
        {
            return;
        }

        code.AppendLineAt(3, "if (__sparse_child_sessions is not null)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "for (var leftIndex = 0; leftIndex < __sparse_child_sessions.Count; leftIndex++)"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "var left = __sparse_child_sessions[leftIndex]; if (!left.HasChanges()) continue;"
        );
        code.AppendLineAt(
            5,
            "for (var rightIndex = leftIndex + 1; rightIndex < __sparse_child_sessions.Count; rightIndex++)"
        );
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "var right = __sparse_child_sessions[rightIndex]; if (!right.HasChanges()) continue;"
        );
        code.AppendLineAt(
            6,
            "if ("
                + childPathOverlap("left.Path", "right.Path")
                + ") { candidate = null; hasPendingChildChanges = false; conflicts = null; return false; }"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
    }

    internal static void AppendCoordinator(
        SharedIndentedBuilder code,
        string modelType,
        string conflictType,
        SparseGeneratorConfig config,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect
    )
    {
        if (
            !SparseEditSessionEmitter.HasChildSessionSurface(config, patchDialect)
            || config.EditSessionWithChildrenInterfaceMetadataName is not { Length: > 0 }
        )
        {
            return;
        }

        code.AppendLineAt(
            2,
            "/// <summary>Gets whether a child session or descendant has pending changes.</summary>"
        );
        code.AppendLineAt(2, "public bool HasPendingChildSessions");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "get");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (__sparse_child_sessions is null) return false;");
        code.AppendLineAt(
            4,
            "foreach (var child in __sparse_child_sessions) if (child.HasChanges()) return true;"
        );
        code.AppendLineAt(4, "return false;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Applies this session and its pending child sessions to a separate model candidate.</summary>"
        );
        code.AppendLineAt(2, "/// <param name=\"current\">The model state to rebase onto.</param>");
        code.AppendLineAt(2, "/// <param name=\"updated\">The candidate model on success.</param>");
        code.AppendLineAt(
            2,
            "/// <param name=\"conflicts\">Structured conflicts when application fails.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <returns>True when all pending changes were applied; otherwise false.</returns>"
        );
        code.AppendLineAt(
            2,
            "public bool TryApplyPendingState("
                + modelType
                + " current, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out "
                + modelType
                + "? updated, out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (current is null) throw new global::System.ArgumentNullException(nameof(current));"
        );
        code.AppendLineAt(3, "var candidate = current;");
        code.AppendLineAt(3, "var ownChanges = PreviewChangeSet();");
        code.AppendLineAt(
            3,
            "if (!ownChanges.IsEmpty && !ownChanges.TryApplyTo(current, out candidate, out conflicts)) { updated = null; return false; }"
        );
        code.AppendLineAt(3, "if (__sparse_child_sessions is not null)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "foreach (var child in __sparse_child_sessions)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "if (!child.HasChanges()) continue;");
        code.AppendLineAt(5, "var result = child.TryApply(candidate);");
        code.AppendLineAt(
            5,
            "if (!result.Accepted) { updated = null; conflicts = result.Conflicts; return false; }"
        );
        code.AppendLineAt(5, "candidate = result.Updated!;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "updated = candidate;");
        code.AppendLineAt(3, "conflicts = null;");
        code.AppendLineAt(3, "return true;");
        code.AppendLineAt(2, "}");
        var statusType = config.EditChildSessionStatusType!;
        code.AppendLineAt(
            2,
            "/// <summary>Gets the status of a registered child session for a typed path.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"path\">The generated path to the child model.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <returns>The status of a child session registered for the path.</returns>"
        );
        code.AppendLineAt(
            2,
            "public "
                + statusType
                + " GetChildSessionStatus<TChild, TChildPaths, TChildSession>(global::"
                + config.EditChildPathInterfaceMetadataName
                + "<"
                + modelType
                + ", TChild, TChildPaths, TChildSession> path)"
        );
        code.AppendLineAt(
            2,
            "where TChild : class where TChildPaths : "
                + patchDialect.TypedPathInterfaceType
                + "<TChild, TChild> where TChildSession : global::"
                + config.EditSessionWithPathsInterfaceMetadataName
                + "<TChild, TChildPaths>"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (path is null) throw new global::System.ArgumentNullException(nameof(path));"
        );
        code.AppendLineAt(3, "var found = false;");
        code.AppendLineAt(3, "if (__sparse_child_sessions is not null)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "foreach (var child in __sparse_child_sessions)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "if (!child.Path.Equals(path.UntypedPath)) continue;");
        code.AppendLineAt(5, "found = true;");
        code.AppendLineAt(
            5,
            "if (child.IsTargetMissing()) return " + statusType + ".TargetMissing;"
        );
        code.AppendLineAt(5, "if (child.HasChanges()) return " + statusType + ".OpenChanged;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "return found ? " + statusType + ".OpenUnchanged : " + statusType + ".NotOpen;"
        );
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Checks pending child and descendant merges without changing session state.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Success and failure both leave values, baselines, histories, and registrations unchanged. A missing target reports failure with null conflicts and preserves the draft.</remarks>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"conflicts\">Structured conflicts when a child merge cannot be prepared.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <returns>True when all pending merges are conflict-free; otherwise false.</returns>"
        );
        code.AppendLineAt(
            2,
            "public bool TryPreflightChildSessions(out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "return TryBuildChildAcceptanceCandidate(out _, out _, out conflicts);"
        );
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Attempts to merge every pending child session into this session.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Validates every descendant onto one detached candidate, commits it with a single write, then acknowledges children parent-first. Typed conflicts and missing targets fail without mutating parent, child, or descendant state; arbitrary setter or notification exceptions propagate after a best-effort model rollback with no child acknowledged. A rolled-back attempt may still appear in the parent history, and edits made from commit notifications observe undefined freshness.</remarks>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"conflicts\">Conflicts when a child could not be merged.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <returns>True when all pending children were merged; otherwise false.</returns>"
        );
        code.AppendLineAt(
            2,
            "public bool TryAcceptChildSessions(out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (!TryBuildChildAcceptanceCandidate(out var candidate, out var hasPendingChildChanges, out conflicts)) return false;"
        );
        code.AppendLineAt(
            3,
            "if (!hasPendingChildChanges || candidate is null) { conflicts = null; return true; }"
        );
        code.AppendLineAt(3, "var liveModel = _session.GetModelForFrameworkAccess();");
        code.AppendLineAt(
            3,
            "var snapshotModel = " + modelType + ".Fragment.From(liveModel).ToModel();"
        );
        code.AppendLineAt(
            3,
            "var aggregateChanges = " + modelType + ".ChangeSet.Between(liveModel, candidate);"
        );
        code.AppendLineAt(3, "if (aggregateChanges.IsEmpty) { conflicts = null; return true; }");
        code.AppendLineAt(3, "try");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (!_session.TryApplyInPlace(aggregateChanges, out conflicts)) return false;"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "catch (global::System.Exception commitFailure)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "global::System.Exception? restoreFailure = null;");
        code.AppendLineAt(4, "try");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "var restoreChanges = "
                + modelType
                + ".ChangeSet.Between(_session.GetModelForFrameworkAccess(), snapshotModel);"
        );
        code.AppendLineAt(
            5,
            "if (!restoreChanges.IsEmpty && !_session.TryApplyInPlace(restoreChanges, out _)) throw new global::System.InvalidOperationException(\"The pre-commit model could not be restored after a failed child session commit.\");"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "catch (global::System.Exception restoreException)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "restoreFailure = restoreException;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "if (restoreFailure is not null) throw new global::System.AggregateException(\"The child session commit failed and restoring the pre-commit model failed.\", commitFailure, restoreFailure);"
        );
        code.AppendLineAt(4, "throw;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "ConfirmChildSessions();");
        code.AppendLineAt(3, "conflicts = null;");
        code.AppendLineAt(3, "return true;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Acknowledges staged child writes after their parent commit succeeded.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Coordinator use only: prefer <see cref=\"TryAcceptChildSessions\"/>. Runs parent-first so descendant confirmations observe committed state. Throws when a target cannot be confirmed; already-confirmed children stay confirmed, so callers may retry after restoring the target.</remarks>"
        );
        code.AppendLineAt(2, "public void ConfirmChildSessions()");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (__sparse_child_sessions is not null)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "foreach (var child in __sparse_child_sessions)");
        code.AppendLineAt(4, "if (child.HasChanges()) child.Confirm();");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Discards pending edits in every registered child session.</summary>"
        );
        code.AppendLineAt(2, "public void DiscardChildSessions()");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (__sparse_child_sessions is not null)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "foreach (var child in __sparse_child_sessions)");
        code.AppendLineAt(4, "if (child.HasChanges()) child.Discard();");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
    }

    internal static void AppendCandidateBuilder(
        SharedIndentedBuilder code,
        string modelType,
        string conflictType,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect
    )
    {
        code.AppendLineAt(
            2,
            "/// <summary>Projects every pending child and descendant edit onto a detached candidate.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Pure projection shared by preflight, acceptance, and submission previews.</remarks>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"candidate\">The candidate model on success; otherwise null.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"hasPendingChildChanges\">True when at least one child has pending changes.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"conflicts\">Structured conflicts when a child merge cannot be prepared.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <returns>True when all pending merges are conflict-free; otherwise false.</returns>"
        );
        code.AppendLineAt(
            2,
            "private bool TryBuildChildAcceptanceCandidate(out "
                + modelType
                + "? candidate, out bool hasPendingChildChanges, out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts)"
        );
        code.AppendLineAt(2, "{");
        AppendChildOverlapGuard(code, patchDialect);
        code.AppendLineAt(3, "candidate = _session.GetModelForFrameworkAccess();");
        code.AppendLineAt(3, "hasPendingChildChanges = false;");
        code.AppendLineAt(3, "if (__sparse_child_sessions is not null)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "foreach (var child in __sparse_child_sessions)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "if (!child.HasChanges()) continue;");
        code.AppendLineAt(5, "hasPendingChildChanges = true;");
        code.AppendLineAt(5, "var result = child.TryApply(candidate);");
        code.AppendLineAt(
            5,
            "if (!result.Accepted) { candidate = null; hasPendingChildChanges = false; conflicts = result.Conflicts; return false; }"
        );
        code.AppendLineAt(5, "candidate = result.Updated!;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "conflicts = null;");
        code.AppendLineAt(3, "return true;");
        code.AppendLineAt(2, "}");
    }

    internal static void AppendCoordinatorStorage(
        SharedIndentedBuilder code,
        string modelType,
        string conflictType,
        string pathType,
        SparseGeneratorConfig config,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect
    )
    {
        if (
            !SparseEditSessionEmitter.HasChildSessionSurface(config, patchDialect)
            || config.EditSessionWithChildrenInterfaceMetadataName is not { Length: > 0 }
        )
        {
            return;
        }

        code.AppendLineAt(
            2,
            "private global::System.Collections.Generic.List<"
                + ChildRegistrationTuple(pathType, modelType, conflictType)
                + ">? __sparse_child_sessions;"
        );
    }

    internal static void AppendOpenMethod(
        SharedIndentedBuilder code,
        string modelType,
        string sessionSimple,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect,
        SparseGeneratorConfig config
    )
    {
        if (
            !SparseEditSessionEmitter.HasChildSessionSurface(config, patchDialect)
            || config.EditChildPathInterfaceMetadataName is not { Length: > 0 } pathInterfaceName
            || config.EditChildSessionInterfaceMetadataName
                is not { Length: > 0 } childSessionInterfaceName
            || config.EditChildSessionImplementationType is not { Length: > 0 } implementationType
        )
        {
            return;
        }

        var pathInterface =
            "global::"
            + pathInterfaceName
            + "<"
            + modelType
            + ", TChild, TChildPaths, TChildSession>";
        var childSessionInterface =
            "global::"
            + childSessionInterfaceName
            + "<TChild, TChildPaths, "
            + patchDialect.ConflictType
            + ", TChildSession"
            + ">";
        var pathType = SparseFragmentPatchEmitter.GetPathType(patchDialect);
        code.AppendLineAt(
            2,
            "/// <summary>Opens a path-addressed child model as an independent edit session.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>The child has its own typed paths and history. Accepting it merges the child draft into this session and resets the child's history baseline.</remarks>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"path\">The generated path to the child model.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <returns>A child edit session connected to this session.</returns>"
        );
        code.AppendLineAt(
            2,
            "public "
                + childSessionInterface
                + " OpenChildSession<TChild, TChildPaths, TChildSession>("
                + pathInterface
                + " path) where TChild : class where TChildPaths : "
                + patchDialect.TypedPathInterfaceType
                + "<TChild, TChild> where TChildSession : global::"
                + config.EditSessionWithPathsInterfaceMetadataName
                + "<TChild, TChildPaths>"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (path is null) throw new global::System.ArgumentNullException(nameof(path));"
        );
        code.AppendLineAt(3, "var draft = CreateChildDraft(path);");
        code.AppendLineAt(3, "var baseline = path.Clone(draft.Model);");
        code.AppendLineAt(
            3,
            "var child = path.CreateEditSession(baseline, draft.Model, draft.RefreshParentDraft);"
        );
        code.AppendLineAt(3, "global::System.Action? detach = null;");
        code.AppendLineAt(
            3,
            "var connected = new "
                + implementationType
                + "<TChild, TChildPaths, "
                + patchDialect.ConflictType
                + ", TChildSession>(child, path.CreateRootPaths(), baseline, path.Clone, _ => { if (!draft.TryAcceptIntoParent(out var conflicts)) return (false, (TChild?)null, conflicts); return (true, draft.Model, null); }, draft.RefreshParentDraft, () => { draft.Dispose(); detach?.Invoke(); }, () => { var accepted = draft.TryPreflightIntoParent(out var conflicts); return (accepted, conflicts); }, () => { draft.ConfirmAccept(); return draft.Model; }, () => draft.IsTargetMissing, () => draft.IsDetached);"
        );
        code.AppendLineAt(
            3,
            "var registration = (Path: path.UntypedPath, HasChanges: (global::System.Func<bool>)(() => connected.HasChanges), TryPreflight: (global::System.Func<(bool Accepted, global::System.Collections.Generic.IReadOnlyList<"
                + patchDialect.ConflictType
                + ">? Conflicts)>)(() => { var accepted = connected.TryPreflightAcceptChanges(out var conflicts); return (accepted, conflicts); }), TryAccept: (global::System.Func<(bool Accepted, global::System.Collections.Generic.IReadOnlyList<"
                + patchDialect.ConflictType
                + ">? Conflicts)>)(() => { var accepted = connected.TryAcceptChanges(out var conflicts); return (accepted, conflicts); }), TryApply: (global::System.Func<"
                + modelType
                + ", (bool Accepted, "
                + modelType
                + "? Updated, global::System.Collections.Generic.IReadOnlyList<"
                + patchDialect.ConflictType
                + ">? Conflicts)>)(current => { var candidate = "
                + modelType
                + ".Fragment.From(current).ToModel(); var candidateSession = new "
                + sessionSimple
                + "(candidate, candidate, null); if (candidateSession.Descriptors.ResolveModel(path.UntypedPath) is not TChild target) return (false, ("
                + modelType
                + "?)null, null); if (!connected.TryApplyPendingState(target, out var updatedChild, out var childConflicts)) { if (childConflicts is not null) { var prefixed = new global::System.Collections.Generic.List<"
                + patchDialect.ConflictType
                + ">(childConflicts.Count); foreach (var conflict in childConflicts) prefixed.Add("
                + patchDialect.ChildConflictPathPrefixExpression!("conflict", "path.UntypedPath")
                + "); childConflicts = prefixed; } return (false, ("
                + modelType
                + "?)null, childConflicts); } path.WriteModelValueInPlace(target, updatedChild!); return (true, candidate, null); }), Discard: (global::System.Action)(connected.DiscardChanges), Confirm: (global::System.Action)(() => connected.ConfirmAccept()), IsTargetMissing: (global::System.Func<bool>)(() => connected.IsTargetMissing));"
        );
        code.AppendLineAt(3, "detach = () => __sparse_child_sessions?.Remove(registration);");
        code.AppendLineAt(
            3,
            "__sparse_child_sessions ??= new global::System.Collections.Generic.List<"
                + ChildRegistrationTuple(pathType, modelType, patchDialect.ConflictType)
                + ">();"
        );
        code.AppendLineAt(3, "__sparse_child_sessions.Add(registration);");
        code.AppendLineAt(3, "return connected;");
        code.AppendLineAt(2, "}");
    }

    /// <summary>Appends the fork/merge facade members shared by both session placements.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="core">Qualified reusable core type for this model.</param>
    /// <param name="conflictType">Qualified structured conflict type.</param>
    internal static void AppendForkMergeMembers(
        SharedIndentedBuilder code,
        string core,
        string conflictType,
        string sessionSimple = "EditSession"
    )
    {
        // Public facade first; the internal core-backed ctor trails with the
        // other internal ctors so generated members stay public-first ordered.
        code.AppendLineAt(
            2,
            "/// <summary>Creates an independent speculative branch of this session.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>The fork uses the same session type with a private mutable copy of the current model and a baseline equal to the current state at fork time. Edits on either side stay isolated and the fork starts without the change callback. This is not a transaction: use <see cref=\"TryMergeFrom\"/> to reconcile a fork, <see cref=\"BatchEdit\"/> to group notifications, <see cref=\"AcceptChanges()\"/> to acknowledge a save, and <see cref=\"Reload\"/> to rebase onto server state.</remarks>"
        );
        code.AppendLineAt(
            2,
            "/// <returns>A new session with its own live model, baseline, and bindings.</returns>"
        );
        code.AppendLineAt(
            2,
            "public " + sessionSimple + " Fork() => new " + sessionSimple + "(_session.Fork());"
        );
        code.AppendLineAt(
            2,
            "/// <summary>Attempts to merge a fork created by <see cref=\"Fork\"/> without modifying either session.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>The fork transition is rebased onto this session's current model. On success the returned session retains this session's original baseline with the merged model as its current state. A conflict leaves both sessions unchanged. Merging an unrelated session or the session itself throws; both live models are read without locking.</remarks>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"fork\">A session created by <see cref=\"Fork\"/> in the same lineage.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"merged\">The new session on success; otherwise null.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"conflicts\">Structured rebase conflicts on failure; otherwise null.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <returns><see langword=\"true\"/> when the fork merged cleanly; otherwise <see langword=\"false\"/>.</returns>"
        );
        code.AppendLineAt(
            2,
            "public bool TryMergeFrom("
                + sessionSimple
                + " fork, out "
                + sessionSimple
                + "? merged, out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (fork is null) throw new global::System.ArgumentNullException(nameof(fork));"
        );
        code.AppendLineAt(
            3,
            "if (_session.TryMergeFrom(fork._session, out var mergedCore, out conflicts) && mergedCore is not null) { merged = new "
                + sessionSimple
                + "(mergedCore); return true; }"
        );
        code.AppendLineAt(3, "merged = null;");
        code.AppendLineAt(3, "return false;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "internal " + sessionSimple + "(" + core + " session) { _session = session; }"
        );
    }

    /// <summary>Appends the public child-session API.</summary>
    internal static void AppendChildSessionApiMembers(
        SharedIndentedBuilder code,
        string modelType,
        string sessionSimple,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect,
        SparseDescriptorDialect? descriptorDialect,
        SparseGeneratorConfig config
    )
    {
        if (descriptorDialect is null || patchDialect.TypedPathInterfaceType is not { Length: > 0 })
        {
            return;
        }

        SparseEditSessionChildEmitter.AppendCoordinator(
            code,
            modelType,
            patchDialect.ConflictType,
            config,
            patchDialect
        );
        SparseEditSessionChildEmitter.AppendOpenMethod(
            code,
            modelType,
            sessionSimple,
            patchDialect,
            config
        );
    }

    /// <summary>Appends the private path-addressed draft used by child sessions.</summary>
    internal static void AppendChildDraftMembers(
        SharedIndentedBuilder code,
        string modelType,
        string sessionSimple,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect,
        SparseDescriptorDialect? descriptorDialect,
        SparseGeneratorConfig config
    )
    {
        if (descriptorDialect is null || patchDialect.TypedPathInterfaceType is not { Length: > 0 })
        {
            return;
        }

        var pathType = SparseFragmentPatchEmitter.GetPathType(patchDialect);
        var conflictType = patchDialect.ConflictType;
        var typedPathInterface =
            patchDialect.TypedPathInterfaceType + "<" + modelType + ", TChild>";
        SparseEditSessionChildEmitter.AppendCoordinatorStorage(
            code,
            modelType,
            conflictType,
            pathType,
            config,
            patchDialect
        );

        code.AppendLineAt(2, "private enum ChildDraftState");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "Open,");
        code.AppendLineAt(3, "TargetMissing,");
        code.AppendLineAt(3, "ParentInvalidated,");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "private ChildDraft<TChild> CreateChildDraft<TChild>("
                + typedPathInterface
                + " path) where TChild : class"
        );
        code.AppendLineAt(2, "{");
        // Validate paths before Fork to avoid cloning on failure; resolve again
        // on the fork to obtain its owned target instance.
        code.AppendLineAt(
            3,
            "if (path is null) throw new global::System.ArgumentNullException(nameof(path));"
        );
        code.AppendLineAt(
            3,
            "if (Descriptors.ResolveModel(path.UntypedPath) is not TChild) throw new global::System.ArgumentException(\"The path does not resolve to a model value of the requested type.\", nameof(path));"
        );
        code.AppendLineAt(
            3,
            "if (!path.CanWriteModelValueInPlace) throw new global::System.ArgumentException(\"The path does not address a generated model value.\", nameof(path));"
        );
        code.AppendLineAt(3, "var fork = Fork();");
        code.AppendLineAt(
            3,
            "if (fork.Descriptors.ResolveModel(path.UntypedPath) is not TChild target) throw new global::System.ArgumentException(\"The path does not resolve to a model value of the requested type.\", nameof(path));"
        );
        code.AppendLineAt(
            3,
            "return new ChildDraft<TChild>(this, fork, path.UntypedPath, path.WriteModelValueInPlace, target, null, null);"
        );
        code.AppendLineAt(2, "}");
        code.AppendLineAt(2, "private sealed class ChildDraft<TChild> where TChild : class");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "/// <summary>Gets the editable child model.</summary>");
        code.AppendLineAt(3, "public TChild Model => _model;");
        code.AppendLineAt(3, "/// <summary>Gets the writable draft model.</summary>");
        code.AppendLineAt(3, "public TChild Observable => _model;");
        code.AppendLineAt(3, "/// <summary>Gets the current draft model.</summary>");
        code.AppendLineAt(3, "public TChild Current => _model;");
        code.AppendLineAt(
            3,
            "/// <summary>Attempts to merge the draft into the parent's current unsaved state.</summary>"
        );
        code.AppendLineAt(
            3,
            "/// <param name=\"conflicts\">Structured conflicts when the child session cannot be accepted.</param>"
        );
        code.AppendLineAt(
            3,
            "/// <returns><see langword=\"true\"/> when the child session was accepted; otherwise <see langword=\"false\"/>.</returns>"
        );
        code.AppendLineAt(
            3,
            "/// <exception cref=\"global::System.InvalidOperationException\">The child session is disconnected, its parent is invalid, or the parent and child belong to unrelated lineages.</exception>"
        );
        code.AppendLineAt(
            3,
            "public bool TryAcceptIntoParent(out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (_state != ChildDraftState.Open) throw new global::System.InvalidOperationException(\"The child edit session is no longer connected.\");"
        );
        code.AppendLineAt(
            4,
            "if (!_parent._session.IsInSameLineage(_fork._session) || (_parentIsOpen is not null && !_parentIsOpen())) { Close(ChildDraftState.ParentInvalidated); throw new global::System.InvalidOperationException(\"The parent edit session is no longer connected or belongs to an unrelated edit lineage.\"); }"
        );
        code.AppendLineAt(
            4,
            "if (_parent.Descriptors.ResolveModel(_path) is not TChild) { conflicts = null; return false; }"
        );
        code.AppendLineAt(
            4,
            "if (_fork.CreateChangeSet().IsEmpty) { conflicts = null; return true; }"
        );
        code.AppendLineAt(4, "var changes = _fork.CreateChangeSet();");
        code.AppendLineAt(
            4,
            "var rebased = changes.RebaseOnto(_parent._session.GetModelForFrameworkAccess());"
        );
        code.AppendLineAt(
            4,
            "if (rebased.HasConflicts) { conflicts = rebased.Conflicts; return false; }"
        );
        code.AppendLineAt(
            4,
            "if (!rebased.Rebased.IsEmpty) { var candidate = _parent.Fork(); if (!candidate.TryApplyInPlace(rebased.Rebased, out conflicts)) return false; if (_parent.Descriptors.ResolveModel(_path) is not TChild currentTarget || candidate.Descriptors.ResolveModel(_path) is not TChild updatedTarget) { conflicts = null; return false; } try { _writeModelValue(currentTarget, updatedTarget); } finally { _parent._session.RefreshAfterExternalMutation(); } }"
        );
        code.AppendLineAt(
            4,
            "if (_parent.Descriptors.ResolveModel(_path) is not TChild latestTarget || _fork.Descriptors.ResolveModel(_path) is not TChild draftTarget) { conflicts = null; return false; }"
        );
        code.AppendLineAt(4, "_writeModelValue(draftTarget, latestTarget);");
        code.AppendLineAt(4, "_fork.AcceptChanges();");
        code.AppendLineAt(4, "conflicts = null;");
        code.AppendLineAt(4, "return true;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "/// <summary>Checks whether the draft can be merged without changing its parent.</summary>"
        );
        code.AppendLineAt(
            3,
            "/// <param name=\"conflicts\">Structured conflicts when the draft cannot be merged.</param>"
        );
        code.AppendLineAt(
            3,
            "/// <returns>True when the draft is conflict-free; otherwise false.</returns>"
        );
        code.AppendLineAt(
            3,
            "public bool TryPreflightIntoParent(out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (_state != ChildDraftState.Open) throw new global::System.InvalidOperationException(\"The child edit session is no longer connected.\");"
        );
        code.AppendLineAt(
            4,
            "if (!_parent._session.IsInSameLineage(_fork._session) || (_parentIsOpen is not null && !_parentIsOpen())) { Close(ChildDraftState.ParentInvalidated); throw new global::System.InvalidOperationException(\"The parent edit session is no longer connected or belongs to an unrelated edit lineage.\"); }"
        );
        code.AppendLineAt(
            4,
            "if (_parent.Descriptors.ResolveModel(_path) is not TChild) { conflicts = null; return false; }"
        );
        code.AppendLineAt(4, "var changes = _fork.CreateChangeSet();");
        code.AppendLineAt(4, "if (changes.IsEmpty) { conflicts = null; return true; }");
        code.AppendLineAt(
            4,
            "var rebased = changes.RebaseOnto(_parent._session.GetModelForFrameworkAccess());"
        );
        code.AppendLineAt(
            4,
            "if (rebased.HasConflicts) { conflicts = rebased.Conflicts; return false; }"
        );
        code.AppendLineAt(4, "if (!rebased.Rebased.IsEmpty)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "var candidate = _parent.Fork();");
        code.AppendLineAt(
            5,
            "if (!candidate.TryApplyInPlace(rebased.Rebased, out conflicts)) return false;"
        );
        code.AppendLineAt(
            5,
            "if (_parent.Descriptors.ResolveModel(_path) is not TChild || candidate.Descriptors.ResolveModel(_path) is not TChild) { conflicts = null; return false; }"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "conflicts = null;");
        code.AppendLineAt(4, "return true;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "/// <summary>Gets whether the draft left the open state.</summary>");
        code.AppendLineAt(3, "public bool IsDetached => _state != ChildDraftState.Open;");
        code.AppendLineAt(
            3,
            "/// <summary>Gets whether the addressed path currently resolves to no model.</summary>"
        );
        code.AppendLineAt(
            3,
            "/// <remarks>The draft and its pending edits are retained while the target is missing.</remarks>"
        );
        code.AppendLineAt(
            3,
            "public bool IsTargetMissing => _state == ChildDraftState.Open && (_parentIsOpen is null || _parentIsOpen()) && _parent.Descriptors.ResolveModel(_path) is not TChild;"
        );
        code.AppendLineAt(
            3,
            "/// <summary>Acknowledges a committed merge without rewriting the parent.</summary>"
        );
        code.AppendLineAt(
            3,
            "/// <remarks>Coordinator use only: the parent commit already wrote these values. Throws without closing the draft when the target cannot be confirmed, so callers may retry after restoring the target.</remarks>"
        );
        code.AppendLineAt(
            3,
            "/// <exception cref=\"global::System.InvalidOperationException\">The draft is no longer connected, its parent is invalid, or its target is missing.</exception>"
        );
        code.AppendLineAt(3, "public void ConfirmAccept()");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (_state != ChildDraftState.Open) throw new global::System.InvalidOperationException(\"The child edit session is no longer connected.\");"
        );
        code.AppendLineAt(
            4,
            "if (!_parent._session.IsInSameLineage(_fork._session) || (_parentIsOpen is not null && !_parentIsOpen())) throw new global::System.InvalidOperationException(\"The parent edit session is no longer connected or belongs to an unrelated edit lineage.\");"
        );
        code.AppendLineAt(
            4,
            "if (_parent.Descriptors.ResolveModel(_path) is not TChild latestTarget || _fork.Descriptors.ResolveModel(_path) is not TChild draftTarget) throw new global::System.InvalidOperationException(\"The child edit session target is no longer available.\");"
        );
        code.AppendLineAt(4, "_writeModelValue(draftTarget, latestTarget);");
        code.AppendLineAt(4, "_fork.AcceptChanges();");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "internal ChildDraft("
                + sessionSimple
                + " parent, "
                + sessionSimple
                + " fork, "
                + pathType
                + " path, global::System.Action<TChild, TChild> writeModelValue, TChild model, global::System.Func<bool>? parentIsOpen, global::System.Func<global::System.Action, global::System.Action>? registerNestedScope) { _parent = parent; _fork = fork; _path = path; _writeModelValue = writeModelValue; _model = model; _parentIsOpen = parentIsOpen; _removeFromParent = registerNestedScope?.Invoke(InvalidateFromParent); _state = ChildDraftState.Open; }"
        );
        code.AppendLineAt(
            3,
            "internal void RefreshParentDraft() => _fork._session.RefreshAfterExternalMutation();"
        );
        code.AppendLineAt(
            3,
            "internal void Dispose() => Close(ChildDraftState.ParentInvalidated);"
        );
        code.AppendLineAt(
            3,
            "private ChildDraft<TNested> CreateChildDraft<TNested>("
                + patchDialect.TypedPathInterfaceType
                + "<TChild, TNested> path) where TNested : class"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (!IsOpenInAncestry) throw new global::System.InvalidOperationException(\"The parent child edit session is no longer connected.\");"
        );
        code.AppendLineAt(
            4,
            "if (path is null) throw new global::System.ArgumentNullException(nameof(path));"
        );
        code.AppendLineAt(4, "var nestedPath = _path.Append(path.UntypedPath);");
        // Same clone-avoidance as the root scope: the draft already exists, so
        // resolving against it validates the path without another full fork.
        code.AppendLineAt(
            4,
            "if (_fork.Descriptors.ResolveModel(nestedPath) is not TNested) throw new global::System.ArgumentException(\"The path does not resolve to a model value of the requested type.\", nameof(path));"
        );
        code.AppendLineAt(
            4,
            "if (!path.CanWriteModelValueInPlace) throw new global::System.ArgumentException(\"The path does not address a generated model value.\", nameof(path));"
        );
        code.AppendLineAt(4, "var fork = _fork.Fork();");
        code.AppendLineAt(
            4,
            "if (fork.Descriptors.ResolveModel(nestedPath) is not TNested target) throw new global::System.ArgumentException(\"The path does not resolve to a model value of the requested type.\", nameof(path));"
        );
        code.AppendLineAt(
            4,
            "return new ChildDraft<TNested>(_fork, fork, nestedPath, path.WriteModelValueInPlace, target, () => IsOpenInAncestry, RegisterNestedScope);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "private readonly " + sessionSimple + " _parent;");
        code.AppendLineAt(3, "private readonly " + sessionSimple + " _fork;");
        code.AppendLineAt(3, "private readonly " + pathType + " _path;");
        code.AppendLineAt(
            3,
            "private readonly global::System.Action<TChild, TChild> _writeModelValue;"
        );
        code.AppendLineAt(3, "private readonly global::System.Func<bool>? _parentIsOpen;");
        code.AppendLineAt(3, "private readonly TChild _model;");
        code.AppendLineAt(
            3,
            "private global::System.Collections.Generic.List<global::System.Action>? _nestedScopes;"
        );
        code.AppendLineAt(3, "private global::System.Action? _removeFromParent;");
        code.AppendLineAt(3, "private ChildDraftState _state;");
        code.AppendLineAt(
            3,
            "private bool IsOpenInAncestry => _state == ChildDraftState.Open && (_parentIsOpen is null || _parentIsOpen());"
        );
        code.AppendLineAt(
            3,
            "private global::System.Action RegisterNestedScope(global::System.Action close) { var nested = _nestedScopes ??= new global::System.Collections.Generic.List<global::System.Action>(); nested.Add(close); return () => nested.Remove(close); }"
        );
        code.AppendLineAt(
            3,
            "private void InvalidateFromParent() => Close(ChildDraftState.ParentInvalidated);"
        );
        code.AppendLineAt(
            3,
            "private void Close(ChildDraftState state) { if (_state != ChildDraftState.Open) return; _state = state; var nested = _nestedScopes; while (nested is { Count: > 0 }) nested[0](); _nestedScopes = null; _removeFromParent?.Invoke(); _removeFromParent = null; }"
        );
        code.AppendLineAt(2, "}");
    }
}
