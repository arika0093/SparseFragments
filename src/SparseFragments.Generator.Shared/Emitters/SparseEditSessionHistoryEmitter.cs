namespace SparseFragments.Generator.Shared;

/// <summary>Opt-in history factory and lifecycle invalidation for edit sessions.</summary>
/// <remarks>
/// Split from the session emitter to keep each file under the size guideline.
/// Both session placements share these helpers so history stays identical.
/// </remarks>
internal static class SparseEditSessionHistoryEmitter
{
    /// <summary>Whether the configured dialect binds a complete history surface.</summary>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="historyType">Reusable history type when bound.</param>
    /// <param name="historyOptionsType">History options type when bound.</param>
    /// <param name="historyHooksType">History hooks type when bound.</param>
    /// <returns>True when the history factory must be emitted.</returns>
    internal static bool TryGetHistoryTypes(
        SparseGeneratorConfig config,
        out string historyType,
        out string historyOptionsType,
        out string historyHooksType
    )
    {
        historyType = string.Empty;
        historyOptionsType = string.Empty;
        historyHooksType = string.Empty;
        var dialect = config.EditSessionDialect;
        if (dialect is null || !dialect.HasHistory)
        {
            return false;
        }

        historyType = dialect.HistoryType!;
        historyOptionsType = dialect.HistoryOptionsType!;
        historyHooksType = dialect.HistoryHooksType!;
        return true;
    }

    /// <summary>Appends baseline lifecycle members with history invalidation.</summary>
    /// <remarks>
    /// Without a history binding the legacy expression-bodied members are
    /// emitted unchanged. With a binding the same members clear registered
    /// histories only after the underlying session operation succeeds, so a
    /// failed accept, reload, reconcile, or revert keeps history intact.
    /// </remarks>
    internal static void AppendLifecycleMembers(
        SharedIndentedBuilder code,
        string modelType,
        string conflictType,
        string rebaseResultType,
        bool hasHistory,
        string historyType,
        string historyOptionsType,
        string historyHooksType
    )
    {
        if (!hasHistory)
        {
            AppendLegacyLifecycleMembers(code, modelType, conflictType, rebaseResultType);
            return;
        }

        code.AppendLineAt(
            2,
            "/// <summary>Reverts pending changes to the retained baseline.</summary>"
        );
        code.AppendLineAt(2, "/// <remarks>Clears registered edit histories on success.</remarks>");
        code.AppendLineAt(2, "public void RevertChanges()");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "_session.RevertChanges();");
        code.AppendLineAt(3, "__SparseClearHistories();");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Reverts the current model to its retained baseline when possible in place.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"conflicts\">Structured conflicts when the pending changes cannot be reverted in place.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <returns><see langword=\"true\"/> when the model was reverted; otherwise <see langword=\"false\"/>.</returns>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Clears registered edit histories only when the revert succeeds.</remarks>"
        );
        code.AppendLineAt(
            2,
            "public bool TryRevertChanges(out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var applied = _session.TryRevertChanges(out conflicts);");
        code.AppendLineAt(3, "if (applied) __SparseClearHistories();");
        code.AppendLineAt(3, "return applied;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Reloads the session around the given server state, preserving local edits.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"serverState\">The authoritative server state.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <returns>The rebase outcome with the rebased change set.</returns>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Clears registered edit histories only when the reload applies without conflicts.</remarks>"
        );
        code.AppendLineAt(
            2,
            "public " + rebaseResultType + " Reload(" + modelType + " serverState)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var outcome = _session.Reload(serverState);");
        code.AppendLineAt(3, "if (!outcome.HasConflicts) __SparseClearHistories();");
        code.AppendLineAt(3, "return outcome;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Accepts the pending changes and advances the baseline.</summary>"
        );
        code.AppendLineAt(2, "/// <remarks>Clears registered edit histories on success.</remarks>");
        code.AppendLineAt(2, "public void AcceptChanges()");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "_session.AcceptChanges();");
        code.AppendLineAt(3, "__SparseClearHistories();");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Accepts the given change set and advances the baseline.</summary>"
        );
        code.AppendLineAt(2, "/// <param name=\"changes\">The change set to accept.</param>");
        code.AppendLineAt(2, "/// <remarks>Clears registered edit histories on success.</remarks>");
        code.AppendLineAt(2, "public void AcceptChanges(" + modelType + ".ChangeSet changes)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "_session.AcceptChanges(changes);");
        code.AppendLineAt(3, "__SparseClearHistories();");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Reconciles a submitted change set with the authoritative persisted model.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Submitted unassigned elements correlate with assigned persisted elements through their shared temporary identities; the persisted model becomes the new baseline with post-submit local edits preserved. Mismatches fail without mutating the session. Clears registered edit histories only when reconciliation succeeds.</remarks>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"submitted\">The submitted transition to acknowledge.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"persisted\">The authoritative persisted model.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"error\">The failure reason, or null on success.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <returns><see langword=\"true\"/> on success; otherwise <see langword=\"false\"/>.</returns>"
        );
        code.AppendLineAt(
            2,
            "public bool TryReconcile("
                + modelType
                + ".ChangeSet submitted, "
                + modelType
                + " persisted, out string? error)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "var reconciled = _session.TryReconcile(submitted, persisted, static (s, p) => "
                + modelType
                + ".__SparseBuildTempMap(s, p), static (live, submittedAfter, persisted, map, pending) => "
                + modelType
                + ".__SparseRetargetTemps(live, submittedAfter, persisted, map!, pending, \"\"), out error);"
        );
        code.AppendLineAt(3, "if (reconciled) __SparseClearHistories();");
        code.AppendLineAt(3, "return reconciled;");
        code.AppendLineAt(2, "}");
        AppendHistoryFactory(
            code,
            modelType,
            conflictType,
            historyType,
            historyOptionsType,
            historyHooksType
        );
    }

    /// <summary>Appends the opt-in history factory for one model session.</summary>
    internal static void AppendHistoryFactory(
        SharedIndentedBuilder code,
        string modelType,
        string conflictType,
        string historyType,
        string historyOptionsType,
        string historyHooksType
    )
    {
        var history = historyType + "<" + modelType + ".ChangeSet>";
        code.AppendLineAt(
            2,
            "/// <summary>Creates an opt-in bounded edit history with undo and redo.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Subscribes to effective transition notifications and stores one typed change set per user action. Undo applies the inverted change set instead of storing two copies. A new ordinary edit clears redo. Replayed operations are never re-recorded. <c>BatchEdit</c> publishes one transition and stores one entry. Without this call the session retains no history. The underlying snapshot and diff work runs whether or not a history exists.</remarks>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"options\">Retention options, or null for defaults.</param>"
        );
        code.AppendLineAt(2, "/// <returns>A disposable history bound to this session.</returns>");
        code.AppendLineAt(
            2,
            "public " + history + " CreateHistory(" + historyOptionsType + "? options = null)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "var hooks = new "
                + historyHooksType
                + "<"
                + modelType
                + ".ChangeSet> { IsEmpty = static changes => changes.IsEmpty, Invert = static changes => changes.Invert(), Compose = static (first, second) => first.Compose(second), TryApply = ("
                + modelType
                + ".ChangeSet changes, out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts) => TryApplyInPlace(changes, out conflicts), Subscribe = handler => TransitionObserved += handler, Unsubscribe = handler => TransitionObserved -= handler };"
        );
        code.AppendLineAt(3, "var history = new " + history + "(options, hooks);");
        code.AppendLineAt(3, "if (__histories is null)");
        code.AppendLineAt(
            4,
            "__histories = new global::System.Collections.Generic.List<" + history + ">();"
        );
        code.AppendLineAt(3, "__histories.Add(history);");
        code.AppendLineAt(
            3,
            "history.Disposed += () => { if (__histories is not null) __histories.Remove(history); };"
        );
        code.AppendLineAt(3, "return history;");
        code.AppendLineAt(2, "}");
    }

    /// <summary>Appends history retention fields for one model session.</summary>
    internal static void AppendHistoryFields(
        SharedIndentedBuilder code,
        string modelType,
        bool hasHistory,
        string historyType
    )
    {
        if (!hasHistory)
        {
            return;
        }

        var history = historyType + "<" + modelType + ".ChangeSet>";
        code.AppendLineAt(
            2,
            "private global::System.Collections.Generic.List<" + history + ">? __histories;"
        );
        code.AppendLineAt(2, "private void __SparseClearHistories()");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var histories = __histories;");
        code.AppendLineAt(3, "if (histories is null) return;");
        code.AppendLineAt(3, "for (var index = 0; index < histories.Count; index++)");
        code.AppendLineAt(4, "histories[index].Clear();");
        code.AppendLineAt(2, "}");
    }

    private static void AppendLegacyLifecycleMembers(
        SharedIndentedBuilder code,
        string modelType,
        string conflictType,
        string rebaseResultType
    )
    {
        code.AppendLineAt(
            2,
            "/// <summary>Reverts pending changes to the retained baseline.</summary>"
        );
        code.AppendLineAt(2, "public void RevertChanges() => _session.RevertChanges();");
        code.AppendLineAt(
            2,
            "/// <summary>Reverts the current model to its retained baseline when possible in place.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"conflicts\">Structured conflicts when the pending changes cannot be reverted in place.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <returns><see langword=\"true\"/> when the model was reverted; otherwise <see langword=\"false\"/>.</returns>"
        );
        code.AppendLineAt(
            2,
            "public bool TryRevertChanges(out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts) => _session.TryRevertChanges(out conflicts);"
        );
        code.AppendLineAt(
            2,
            "/// <summary>Reloads the session around the given server state, preserving local edits.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"serverState\">The authoritative server state.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <returns>The rebase outcome with the rebased change set.</returns>"
        );
        code.AppendLineAt(
            2,
            "public "
                + rebaseResultType
                + " Reload("
                + modelType
                + " serverState) => _session.Reload(serverState);"
        );
        code.AppendLineAt(
            2,
            "/// <summary>Accepts the pending changes and advances the baseline.</summary>"
        );
        code.AppendLineAt(2, "public void AcceptChanges() => _session.AcceptChanges();");
        code.AppendLineAt(
            2,
            "/// <summary>Accepts the given change set and advances the baseline.</summary>"
        );
        code.AppendLineAt(2, "/// <param name=\"changes\">The change set to accept.</param>");
        code.AppendLineAt(
            2,
            "public void AcceptChanges("
                + modelType
                + ".ChangeSet changes) => _session.AcceptChanges(changes);"
        );
        code.AppendLineAt(
            2,
            "/// <summary>Reconciles a submitted change set with the authoritative persisted model.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Submitted unassigned elements correlate with assigned persisted elements through their shared temporary identities; the persisted model becomes the new baseline with post-submit local edits preserved. Mismatches fail without mutating the session.</remarks>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"submitted\">The submitted transition to acknowledge.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"persisted\">The authoritative persisted model.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"error\">The failure reason, or null on success.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <returns><see langword=\"true\"/> on success; otherwise <see langword=\"false\"/>.</returns>"
        );
        code.AppendLineAt(
            2,
            "public bool TryReconcile("
                + modelType
                + ".ChangeSet submitted, "
                + modelType
                + " persisted, out string? error) => _session.TryReconcile(submitted, persisted, static (s, p) => "
                + modelType
                + ".__SparseBuildTempMap(s, p), static (live, submittedAfter, persisted, map, pending) => "
                + modelType
                + ".__SparseRetargetTemps(live, submittedAfter, persisted, map!, pending, \"\"), out error);"
        );
    }
}
