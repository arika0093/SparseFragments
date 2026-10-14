namespace SparseTypes.Generator.Shared;

internal static class SparseEditSessionChildEmitter
{
    internal static void AppendCoordinator(
        SharedIndentedBuilder code,
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
            "/// <summary>Attempts to merge every pending child session into this session.</summary>"
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
        code.AppendLineAt(3, "if (__sparse_child_sessions is not null)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "foreach (var child in __sparse_child_sessions)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "if (!child.HasChanges()) continue;");
        code.AppendLineAt(
            5,
            "var result = child.TryAccept(); if (!result.Accepted) { conflicts = result.Conflicts; return false; }"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "conflicts = null;");
        code.AppendLineAt(3, "return true;");
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

    internal static void AppendCoordinatorStorage(
        SharedIndentedBuilder code,
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
            "private global::System.Collections.Generic.List<(global::System.Func<bool> HasChanges, global::System.Func<(bool Accepted, global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? Conflicts)> TryAccept, global::System.Action Discard)>? __sparse_child_sessions;"
        );
    }

    internal static void AppendOpenMethod(
        SharedIndentedBuilder code,
        string modelType,
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
                + ", TChildSession>(child, path.CreateRootPaths(), baseline, path.Clone, _ => { if (!draft.TryAcceptIntoParent(out var conflicts)) return (false, (TChild?)null, conflicts); return (true, draft.Model, null); }, draft.RefreshParentDraft, () => { draft.Dispose(); detach?.Invoke(); });"
        );
        code.AppendLineAt(
            3,
            "var registration = (HasChanges: (global::System.Func<bool>)(() => connected.HasChanges), TryAccept: (global::System.Func<(bool Accepted, global::System.Collections.Generic.IReadOnlyList<"
                + patchDialect.ConflictType
                + ">? Conflicts)>)(() => { var accepted = connected.TryAcceptChanges(out var conflicts); return (accepted, conflicts); }), Discard: (global::System.Action)(connected.DiscardChanges));"
        );
        code.AppendLineAt(3, "detach = () => __sparse_child_sessions?.Remove(registration);");
        code.AppendLineAt(
            3,
            "__sparse_child_sessions ??= new global::System.Collections.Generic.List<(global::System.Func<bool> HasChanges, global::System.Func<(bool Accepted, global::System.Collections.Generic.IReadOnlyList<"
                + patchDialect.ConflictType
                + ">? Conflicts)> TryAccept, global::System.Action Discard)>();"
        );
        code.AppendLineAt(3, "__sparse_child_sessions.Add(registration);");
        code.AppendLineAt(3, "return connected;");
        code.AppendLineAt(2, "}");
    }
}
