namespace SparseTypes.Generator.Shared;

/// <summary>History type names bound by the configured dialect.</summary>
/// <remarks>Shared never hard-codes a runtime history, options, hooks, or contract type.</remarks>
internal sealed record HistoryBinding
{
    public HistoryBinding(
        string historyType,
        string historyOptionsType,
        string historyHooksType,
        string historyContractType
    )
    {
        this.HistoryType = historyType;
        this.HistoryOptionsType = historyOptionsType;
        this.HistoryHooksType = historyHooksType;
        this.HistoryContractType = historyContractType;
    }

    /// <summary>Reusable history type, such as the typed undo/redo core.</summary>
    public string HistoryType { get; init; }

    /// <summary>History retention options type.</summary>
    public string HistoryOptionsType { get; init; }

    /// <summary>Typed change-set hooks type wiring the concrete change set.</summary>
    public string HistoryHooksType { get; init; }

    /// <summary>Non-generic history control contract, or empty for the legacy factory only.</summary>
    public string HistoryContractType { get; init; }

    /// <summary>Whether the single-owned history surface is bound.</summary>
    public bool HasOwnedHistory => HistoryContractType.Length != 0;
}

/// <summary>Opt-in history factory and session-owned history for edit sessions.</summary>
/// <remarks>Split from the session emitter to keep each file under the size guideline.</remarks>
internal static class SparseEditSessionHistoryEmitter
{
    /// <summary>Binds the configured dialect history surface, if complete.</summary>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="binding">Reusable history names when bound.</param>
    /// <returns>True when the history factory must be emitted.</returns>
    internal static bool TryGetHistoryBinding(
        SparseGeneratorConfig config,
        out HistoryBinding? binding
    )
    {
        binding = null;
        var dialect = config.EditSessionDialect;
        if (dialect is null || !dialect.HasHistory)
        {
            return false;
        }

        binding = new HistoryBinding(
            dialect.HistoryType!,
            dialect.HistoryOptionsType!,
            dialect.HistoryHooksType!,
            dialect.HistoryContractType ?? string.Empty
        );
        return true;
    }

    /// <summary>Appends baseline lifecycle members with history invalidation.</summary>
    /// <remarks>Without a history binding the legacy expression-bodied members are emitted unchanged.</remarks>
    internal static void AppendLifecycleMembers(
        SharedIndentedBuilder code,
        string modelType,
        string conflictType,
        string rebaseResultType,
        HistoryBinding? history
    )
    {
        if (history is null)
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
        AppendHistoryFactory(code, modelType, conflictType, history);
        if (history.HasOwnedHistory)
        {
            AppendOwnedHistoryMembers(code, modelType, conflictType, history);
        }
    }

    /// <summary>Appends the legacy opt-in history factory for one model session.</summary>
    /// <remarks>Advanced multi-observer escape hatch: prefer the single-owned <c>EnableHistory</c> surface for ordinary undo/redo.</remarks>
    private static void AppendHistoryFactory(
        SharedIndentedBuilder code,
        string modelType,
        string conflictType,
        HistoryBinding history
    )
    {
        var historyType = history.HistoryType + "<" + modelType + ".ChangeSet>";
        code.AppendLineAt(
            2,
            "/// <summary>Creates an independent opt-in bounded edit history with undo and redo.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Advanced use only; prefer <see cref=\"EnableHistory\"/> for the single session-owned history. Subscribes to effective transition notifications and stores one typed change set per user action. Undo applies the inverted change set instead of storing two copies. A new ordinary edit clears redo. Replayed operations are never re-recorded. <c>BatchEdit</c> publishes one transition and stores one entry. Without this call the session retains no history. The underlying snapshot and diff work runs whether or not a history exists. The caller owns the returned history and must dispose it.</remarks>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"options\">Retention options, or null for defaults.</param>"
        );
        code.AppendLineAt(2, "/// <returns>A disposable history bound to this session.</returns>");
        code.AppendLineAt(
            2,
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
        );
        code.AppendLineAt(
            2,
            "public "
                + historyType
                + " CreateHistory("
                + history.HistoryOptionsType
                + "? options = null)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "var hooks = " + HistoryHooksExpression(modelType, conflictType, history) + ";"
        );
        code.AppendLineAt(3, "var history = new " + historyType + "(options, hooks);");
        code.AppendLineAt(3, "if (__histories is null)");
        code.AppendLineAt(
            4,
            "__histories = new global::System.Collections.Generic.List<" + historyType + ">();"
        );
        code.AppendLineAt(3, "__histories.Add(history);");
        code.AppendLineAt(
            3,
            "history.Disposed += () => { if (__histories is not null) __histories.Remove(history); };"
        );
        code.AppendLineAt(3, "return history;");
        code.AppendLineAt(2, "}");
    }

    /// <summary>Appends the single-owned history surface for one model session.</summary>
    /// <remarks>The session retains one enabled history behind a non-disposable framework-neutral contract.</remarks>
    private static void AppendOwnedHistoryMembers(
        SharedIndentedBuilder code,
        string modelType,
        string conflictType,
        HistoryBinding history
    )
    {
        var historyType = history.HistoryType + "<" + modelType + ".ChangeSet>";
        code.AppendLineAt(
            2,
            "/// <summary>Gets the session-owned edit history, or null when history is not enabled.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>No history subscription or retained entries exist until <see cref=\"EnableHistory\"/> runs. Forked and new sessions start without history.</remarks>"
        );
        code.AppendLineAt(
            2,
            "public " + history.HistoryContractType + "? History => __ownedHistoryView;"
        );
        code.AppendLineAt(
            2,
            "/// <summary>Occurs when session-owned history availability changes.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Raised after <see cref=\"EnableHistory\"/> creates the history and after <see cref=\"DisableHistory\"/> releases it. Undo/redo stack changes raise <c>StateChanged</c> on the history itself.</remarks>"
        );
        code.AppendLineAt(2, "public event global::System.Action? HistoryChanged;");
        code.AppendLineAt(
            2,
            "/// <summary>Enables the single session-owned edit history with undo and redo.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Subscribes to effective transition notifications and stores one typed change set per user action. Repeating the call with the same options returns the existing history without resetting retained edits. Repeating with different options throws; call <see cref=\"DisableHistory\"/> first to reconfigure. The returned control is owned by the session and cannot be disposed by consumers. Without this call the session retains no history.</remarks>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"options\">Retention options, or null for defaults.</param>"
        );
        code.AppendLineAt(2, "/// <returns>The session-owned history control.</returns>");
        code.AppendLineAt(
            2,
            "public "
                + history.HistoryContractType
                + " EnableHistory("
                + history.HistoryOptionsType
                + "? options = null)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (__ownedHistoryView is not null)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "var requestedMaxEntries = (options ?? new "
                + history.HistoryOptionsType
                + "()).MaxEntries;"
        );
        code.AppendLineAt(4, "if (__ownedHistoryView.MaxEntries != requestedMaxEntries)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "throw new global::System.InvalidOperationException(\"The session already has an enabled history with different options. Call DisableHistory() first to reconfigure instead of silently resetting retained edits.\");"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "return __ownedHistoryView;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "var hooks = " + HistoryHooksExpression(modelType, conflictType, history) + ";"
        );
        code.AppendLineAt(3, "var history = new " + historyType + "(options, hooks);");
        code.AppendLineAt(3, "var view = new __OwnedHistoryView(history);");
        code.AppendLineAt(3, "__ownedHistoryView = view;");
        code.AppendLineAt(3, "var historyChanged = HistoryChanged;");
        code.AppendLineAt(3, "if (historyChanged is not null)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "historyChanged();");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return view;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Disables the session-owned edit history and releases retention.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Unsubscribes from transitions and drops retained entries. Histories created by the legacy factory are unaffected. No-op when history is not enabled.</remarks>"
        );
        code.AppendLineAt(2, "public void DisableHistory()");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var view = __ownedHistoryView;");
        code.AppendLineAt(3, "if (view is null)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "return;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "__ownedHistoryView = null;");
        code.AppendLineAt(3, "view.__SparseDispose();");
        code.AppendLineAt(3, "var historyChanged = HistoryChanged;");
        code.AppendLineAt(3, "if (historyChanged is not null)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "historyChanged();");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
    }

    /// <summary>Builds the typed hooks expression wiring one change set.</summary>
    private static string HistoryHooksExpression(
        string modelType,
        string conflictType,
        HistoryBinding history
    ) =>
        "new "
        + history.HistoryHooksType
        + "<"
        + modelType
        + ".ChangeSet> { IsEmpty = static changes => changes.IsEmpty, Invert = static changes => changes.Invert(), Compose = static (first, second) => first.Compose(second), TryApply = ("
        + modelType
        + ".ChangeSet changes, out global::System.Collections.Generic.IReadOnlyList<"
        + conflictType
        + ">? conflicts) => TryApplyInPlace(changes, out conflicts), Subscribe = handler => TransitionObserved += handler, Unsubscribe = handler => TransitionObserved -= handler }";

    /// <summary>Appends history retention fields for one model session.</summary>
    internal static void AppendHistoryFields(
        SharedIndentedBuilder code,
        string modelType,
        string conflictType,
        HistoryBinding? history
    )
    {
        if (history is null)
        {
            return;
        }

        var historyType = history.HistoryType + "<" + modelType + ".ChangeSet>";
        code.AppendLineAt(
            2,
            "private global::System.Collections.Generic.List<" + historyType + ">? __histories;"
        );
        if (history.HasOwnedHistory)
        {
            code.AppendLineAt(2, "private __OwnedHistoryView? __ownedHistoryView;");
            AppendOwnedHistoryView(code, modelType, conflictType, history);
        }

        code.AppendLineAt(2, "private void __SparseClearHistories()");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var histories = __histories;");
        code.AppendLineAt(3, "if (histories is not null)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "for (var index = 0; index < histories.Count; index++)");
        code.AppendLineAt(5, "histories[index].Clear();");
        code.AppendLineAt(3, "}");
        if (history.HasOwnedHistory)
        {
            code.AppendLineAt(3, "__ownedHistoryView?.Clear();");
        }

        code.AppendLineAt(2, "}");
    }

    /// <summary>Appends the non-disposable session-owned history view.</summary>
    /// <remarks>The view forwards the control contract to the retained typed history and releases it only through the owning session.</remarks>
    private static void AppendOwnedHistoryView(
        SharedIndentedBuilder code,
        string modelType,
        string conflictType,
        HistoryBinding history
    )
    {
        var historyType = history.HistoryType + "<" + modelType + ".ChangeSet>";
        var conflicts =
            "out global::System.Collections.Generic.IReadOnlyList<" + conflictType + ">? conflicts";
        code.AppendLineAt(
            2,
            "/// <summary>Session-owned non-disposable history control.</summary>"
        );
        code.AppendLineAt(
            2,
            "private sealed class __OwnedHistoryView : " + history.HistoryContractType
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "public __OwnedHistoryView(" + historyType + " history)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "_inner = history;");
        code.AppendLineAt(4, "_inner.StateChanged += OnStateChanged;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "public event global::System.Action? StateChanged;");
        code.AppendLineAt(3, "public bool CanUndo => Inner.CanUndo;");
        code.AppendLineAt(3, "public bool CanRedo => Inner.CanRedo;");
        code.AppendLineAt(3, "public int UndoCount => Inner.UndoCount;");
        code.AppendLineAt(3, "public int RedoCount => Inner.RedoCount;");
        code.AppendLineAt(3, "public int MaxEntries => Inner.MaxEntries;");
        code.AppendLineAt(3, "public void Undo() => Inner.Undo();");
        code.AppendLineAt(
            3,
            "public bool TryUndo(" + conflicts + ") => Inner.TryUndo(out conflicts);"
        );
        code.AppendLineAt(3, "public void Redo() => Inner.Redo();");
        code.AppendLineAt(
            3,
            "public bool TryRedo(" + conflicts + ") => Inner.TryRedo(out conflicts);"
        );
        code.AppendLineAt(
            3,
            "public global::System.IDisposable BeginGroup() => Inner.BeginGroup();"
        );
        code.AppendLineAt(
            3,
            "public void Group(global::System.Action action) => Inner.Group(action);"
        );
        code.AppendLineAt(3, "public void Clear() => Inner.Clear();");
        code.AppendLineAt(3, "internal void __SparseDispose()");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var history = _inner;");
        code.AppendLineAt(4, "_inner = null;");
        code.AppendLineAt(4, "if (history is not null)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "history.StateChanged -= OnStateChanged;");
        code.AppendLineAt(5, "history.Dispose();");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "private "
                + historyType
                + " Inner => _inner ?? throw new global::System.ObjectDisposedException(GetType().FullName);"
        );
        code.AppendLineAt(3, "private void OnStateChanged()");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var raised = StateChanged;");
        code.AppendLineAt(4, "if (raised is not null)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "raised();");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "private " + historyType + "? _inner;");
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
