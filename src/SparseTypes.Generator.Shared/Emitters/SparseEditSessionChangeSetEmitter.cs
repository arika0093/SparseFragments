namespace SparseTypes.Generator.Shared;

internal static class SparseEditSessionChangeSetEmitter
{
    internal static void AppendMembers(
        SharedIndentedBuilder code,
        string modelType,
        SparseGeneratorConfig config,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect
    )
    {
        code.AppendLineAt(
            2,
            "/// <summary>Runs observable edits as one transition notification, including nested batches.</summary>"
        );
        code.AppendLineAt(2, "/// <param name=\"edit\">The observable edits to run.</param>");
        code.AppendLineAt(
            2,
            "public void BatchEdit(global::System.Action edit) => _session.BatchEdit(edit);"
        );

        if (SparseEditSessionEmitter.HasChildSessionSurface(config, patchDialect))
        {
            AppendChildSessionChangeSetMembers(code, modelType, patchDialect);
        }
        else
        {
            code.AppendLineAt(
                2,
                "/// <summary>Creates a change set describing the pending changes.</summary>"
            );
            code.AppendLineAt(
                2,
                "/// <returns>The pending change set; empty when there are no changes.</returns>"
            );
            code.AppendLineAt(
                2,
                "public "
                    + modelType
                    + ".ChangeSet CreateChangeSet() => _session.CreateChangeSet();"
            );
            code.AppendLineAt(
                2,
                "/// <summary>Prepares a sendable change set from this session.</summary>"
            );
            code.AppendLineAt(2, "/// <returns>The pending change set.</returns>");
            code.AppendLineAt(
                2,
                "public "
                    + modelType
                    + ".ChangeSet PrepareChangeSet() => _session.CreateChangeSet();"
            );
        }

        code.AppendLineAt(
            2,
            "/// <summary>Creates a read-only snapshot of changes owned by this session.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>This does not accept or otherwise mutate registered child sessions.</remarks>"
        );
        code.AppendLineAt(2, "/// <returns>This session's pending change set.</returns>");
        code.AppendLineAt(
            2,
            "public " + modelType + ".ChangeSet PreviewChangeSet() => _session.CreateChangeSet();"
        );

        if (config.EditSessionWithChangeSetInterfaceMetadataName is { Length: > 0 } adapterName)
        {
            var adapterType =
                "global::" + adapterName + "<" + modelType + ", " + modelType + ".ChangeSet>";
            code.AppendLineAt(
                2,
                "bool "
                    + adapterType
                    + ".IsEmpty("
                    + modelType
                    + ".ChangeSet changes) => changes.IsEmpty;"
            );
            code.AppendLineAt(
                2,
                "global::System.Collections.Generic.IReadOnlyList<"
                    + SparseFragmentPatchEmitter.GetPathType(patchDialect)
                    + "> "
                    + adapterType
                    + ".EnumerateChangedPaths("
                    + modelType
                    + ".ChangeSet changes) => changes.EnumerateChangedPaths();"
            );
        }
    }

    private static void AppendChildSessionChangeSetMembers(
        SharedIndentedBuilder code,
        string modelType,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect
    )
    {
        code.AppendLineAt(
            2,
            "/// <summary>Attempts to prepare a sendable change set by accepting pending child sessions.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"changes\">The change set on success; otherwise null.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"conflicts\">Child-session conflicts on failure.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <returns>True when the change set was prepared; otherwise false.</returns>"
        );
        code.AppendLineAt(
            2,
            "public bool TryCreateChangeSet(out "
                + modelType
                + ".ChangeSet? changes, out global::System.Collections.Generic.IReadOnlyList<"
                + patchDialect.ConflictType
                + ">? conflicts)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (!TryAcceptChildSessions(out conflicts)) { changes = null; return false; }"
        );
        code.AppendLineAt(3, "changes = _session.CreateChangeSet();");
        code.AppendLineAt(3, "return true;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Creates a change set after merging pending child sessions.</summary>"
        );
        code.AppendLineAt(2, "/// <returns>The pending change set.</returns>");
        code.AppendLineAt(
            2,
            "/// <exception cref=\"global::System.InvalidOperationException\">A child session conflicts with this session.</exception>"
        );
        code.AppendLineAt(
            2,
            "public "
                + modelType
                + ".ChangeSet CreateChangeSet() => TryCreateChangeSet(out var changes, out var conflicts) ? changes! : throw new global::System.InvalidOperationException(conflicts is null ? \"A child edit session could not be accepted.\" : \"A child edit session conflicts with changes in its parent session.\");"
        );
        code.AppendLineAt(
            2,
            "/// <summary>Attempts to prepare a sendable change set by accepting pending child sessions.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"changes\">The prepared change set on success; otherwise null.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"conflicts\">Child-session conflicts on failure.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <returns>True when the change set was prepared; otherwise false.</returns>"
        );
        code.AppendLineAt(
            2,
            "public bool TryPrepareChangeSet(out "
                + modelType
                + ".ChangeSet? changes, out global::System.Collections.Generic.IReadOnlyList<"
                + patchDialect.ConflictType
                + ">? conflicts) => TryCreateChangeSet(out changes, out conflicts);"
        );
        code.AppendLineAt(
            2,
            "/// <summary>Prepares a sendable change set by accepting pending child sessions.</summary>"
        );
        code.AppendLineAt(2, "/// <returns>The prepared change set.</returns>");
        code.AppendLineAt(
            2,
            "public " + modelType + ".ChangeSet PrepareChangeSet() => CreateChangeSet();"
        );
    }
}
