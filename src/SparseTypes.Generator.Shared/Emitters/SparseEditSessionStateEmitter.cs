using System.Collections.Immutable;

namespace SparseTypes.Generator.Shared;

/// <summary>Path-first semantic state surface for generated edit sessions (issue #250).</summary>
/// <remarks>
/// Emits live <c>FieldState</c> and <c>CollectionState</c> members on each
/// model session. Field queries accept any generated typed path and project
/// the canonical flattened change records; collection views wrap the member's
/// typed keyed/dictionary transition, so row states reuse the canonical
/// semantics instead of rederiving them. State inspection only: no mutation,
/// no baseline retention beyond the session's own, and no UI references.
/// Distinct from the sibling path-addressed collection editing facade, which
/// keeps the <c>Collection</c> name.
/// </remarks>
internal static class SparseEditSessionStateEmitter
{
    internal static bool HasStateSurface(SparseFragmentPatchEmitter.SparsePatchDialect dialect) =>
        dialect.StateContracts is not null
        && dialect.TransitionContracts is not null
        && dialect.TypedPathInterfaceType is { Length: > 0 };

    internal static void AppendStateMembers(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        if (!HasStateSurface(dialect))
        {
            return;
        }

        var states = dialect.StateContracts!;
        var pathType = SparseFragmentPatchEmitter.GetPathType(dialect);
        AppendFieldState(code, modelType, pathType, states, dialect);
        AppendCollectionState(code, modelType, members, pathType, states);
        AppendObserveMembers(code, modelType, pathType, states, dialect);
    }

    private static void AppendFieldState(
        SharedIndentedBuilder code,
        string modelType,
        string pathType,
        SparseStateContracts states,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        // The concrete path struct (like ChangeSet.Find) keeps overload
        // resolution unambiguous against the model-agnostic overload.
        var typedPath = SparseFragmentPatchEmitter.GetTypedPathType(dialect, modelType, "TValue");
        code.AppendLineAt(
            2,
            "/// <summary>Queries the semantic field state at a typed path against the current baseline.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Live query: derives a fresh change set from the retained baseline on each call and never retains subscriptions or per-field caches. Snapshot <see cref=\"CreateChangeSet\"/> for repeated point-in-time lookups.</remarks>"
        );
        code.AppendLineAt(2, "/// <param name=\"path\">The typed path to inspect.</param>");
        code.AppendLineAt(
            2,
            "/// <typeparam name=\"TValue\">The addressed value type.</typeparam>"
        );
        code.AppendLineAt(
            2,
            "/// <returns>The live field state with presence-aware endpoints.</returns>"
        );
        code.AppendLineAt(
            2,
            "public "
                + states.FieldStateType
                + "<TValue> FieldState<TValue>("
                + typedPath
                + " path)"
        );
        code.AppendLineAt(2, "{");
        if (!dialect.PathIsValueType)
        {
            code.AppendLineAt(
                3,
                "if (path is null) throw new global::System.ArgumentNullException(nameof(path));"
            );
        }

        code.AppendLineAt(
            3,
            "return "
                + states.StateQueryType
                + ".FieldState<"
                + modelType
                + ", TValue>(CreateChangeSet(), path);"
        );
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Queries the semantic field state at a model-agnostic path against the current baseline.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Covers paths without a compile-time typed form, such as nested-model builders and missing or deleted members. Unknown paths report a clean state. See the typed overload for the exact/ancestor/descendant contract.</remarks>"
        );
        code.AppendLineAt(2, "/// <param name=\"path\">The path to inspect.</param>");
        code.AppendLineAt(2, "/// <returns>The live field state with boxed endpoints.</returns>");
        code.AppendLineAt(
            2,
            "public " + states.FieldStateType + "<object?> FieldState(" + pathType + " path)"
        );
        code.AppendLineAt(2, "{");
        if (!dialect.PathIsValueType)
        {
            code.AppendLineAt(
                3,
                "if (path is null) throw new global::System.ArgumentNullException(nameof(path));"
            );
        }

        code.AppendLineAt(
            3,
            "return " + states.StateQueryType + ".FieldState(CreateChangeSet(), path);"
        );
        code.AppendLineAt(2, "}");
    }

    private static void AppendObserveMembers(
        SharedIndentedBuilder code,
        string modelType,
        string pathType,
        SparseStateContracts states,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        if (!states.HasObservedField)
        {
            return;
        }

        // The concrete path struct keeps overload resolution unambiguous
        // against the model-agnostic overload, mirroring FieldState.
        var typedPath = SparseFragmentPatchEmitter.GetTypedPathType(dialect, modelType, "TValue");
        AppendObserveOverload(code, states, typedPath, "TValue", true, dialect);
        AppendObserveOverload(code, states, pathType, "object?", false, dialect);
    }

    private static void AppendObserveOverload(
        SharedIndentedBuilder code,
        SparseStateContracts states,
        string pathParameterType,
        string valueType,
        bool typed,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var kind = typed ? "a typed path" : "a model-agnostic path";
        var typeParameters = typed ? "<TValue>" : string.Empty;
        var watcherType = states.ObservedFieldType + (typed ? "<TValue>" : "<object?>");

        code.AppendLineAt(
            2,
            "/// <summary>Observes the semantic field state at "
                + kind
                + " with binding notifications.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Opt-in live subscription: refreshes on every session state notification (observable edits, batches, in-place apply, revert, both baseline accepts, reload, reconcile) and raises <see cref=\"global::System.ComponentModel.INotifyPropertyChanged.PropertyChanged\"/> only on effective changes, synchronously with no UI thread marshaling. <c>IsChanged</c> is the semantic baseline diff, not a visited flag. Dispose the watcher to unsubscribe. Key segments keep identity across moves; index segments stay positional.</remarks>"
        );
        code.AppendLineAt(2, "/// <param name=\"path\">The path to observe.</param>");
        if (typed)
        {
            code.AppendLineAt(
                2,
                "/// <typeparam name=\"TValue\">The addressed value type.</typeparam>"
            );
        }

        code.AppendLineAt(2, "/// <returns>A disposable watcher bound to this session.</returns>");
        code.AppendLineAt(2, "public " + watcherType + " ObserveField" + typeParameters);
        AppendObserveBody(code, pathParameterType, states, valueType, typed, dialect, true);
        code.AppendLineAt(
            2,
            "/// <summary>Observes the semantic field state at "
                + kind
                + " with binding notifications.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Alias for <c>ObserveField</c>; both spellings return the same watcher.</remarks>"
        );
        code.AppendLineAt(2, "/// <param name=\"path\">The path to observe.</param>");
        if (typed)
        {
            code.AppendLineAt(
                2,
                "/// <typeparam name=\"TValue\">The addressed value type.</typeparam>"
            );
        }

        code.AppendLineAt(2, "/// <returns>A disposable watcher bound to this session.</returns>");
        code.AppendLineAt(2, "public " + watcherType + " WatchField" + typeParameters);
        AppendObserveBody(code, pathParameterType, states, valueType, typed, dialect, false);
    }

    private static void AppendObserveBody(
        SharedIndentedBuilder code,
        string pathParameterType,
        SparseStateContracts states,
        string valueType,
        bool typed,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        bool primary
    )
    {
        code.AppendLineAt(2, "(" + pathParameterType + " path)");
        code.AppendLineAt(2, "{");
        if (!dialect.PathIsValueType)
        {
            code.AppendLineAt(
                3,
                "if (path is null) throw new global::System.ArgumentNullException(nameof(path));"
            );
        }

        if (primary)
        {
            code.AppendLineAt(
                3,
                "var hooks = new "
                    + states.ObservedFieldHooksType
                    + "<"
                    + valueType
                    + "> { Query = () => FieldState(path), Subscribe = handler => PropertyChanged += handler, Unsubscribe = handler => PropertyChanged -= handler };"
            );
            code.AppendLineAt(
                3,
                "return new " + states.ObservedFieldType + "<" + valueType + ">(path, hooks);"
            );
        }
        else
        {
            var typeArguments = typed ? "<TValue>" : string.Empty;
            code.AppendLineAt(3, "return ObserveField" + typeArguments + "(path);");
        }

        code.AppendLineAt(2, "}");
    }

    private static void AppendCollectionState(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        string pathType,
        SparseStateContracts states
    )
    {
        code.AppendLineAt(
            2,
            "/// <summary>Queries the keyed or dictionary collection state at a root collection path.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Live query: derives a fresh change set from the retained baseline on each call; the returned view snapshots that change set. State inspection only: the view never mutates the session. Root collection members only; navigate the change set for nested collections and use <see cref=\"FieldState\"/> for other members.</remarks>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"collectionPath\">The root collection member path.</param>"
        );
        code.AppendLineAt(2, "/// <returns>The live collection state view.</returns>");
        code.AppendLineAt(
            2,
            "/// <exception cref=\"global::System.ArgumentException\">The path does not address a root keyed or dictionary collection member.</exception>"
        );
        code.AppendLineAt(
            2,
            "public "
                + states.CollectionStateType
                + " CollectionState("
                + pathType
                + " collectionPath)"
        );
        code.AppendLineAt(2, "{");
        SparseChangeSetNaming.ComputePublicNames(members, out var propNames, out _);
        var emitted = false;
        foreach (var member in members)
        {
            var isKeyed = SparseChangeSetBasicsEmitter.IsKeyed(member);
            var isDict = SparseChangeSetBasicsEmitter.IsDict(member);
            if (!isKeyed && !isDict)
            {
                continue;
            }

            // Full-path equality keeps dispatch exact (root type, member, and
            // collection kind all match) without naming path segment internals.
            var navigation =
                modelType + ".SparsePath." + SparseNaming.EscapeIdentifier(member.Property.Name);
            var property = SparseNaming.EscapeIdentifier(propNames[member.Id]);
            var factory = isKeyed ? "KeyedState" : "DictionaryState";
            code.AppendLineAt(
                3,
                "if (collectionPath.Equals(("
                    + pathType
                    + ")"
                    + navigation
                    + ")) return "
                    + states.StateQueryType
                    + "."
                    + factory
                    + "(CreateChangeSet()."
                    + property
                    + ", collectionPath);"
            );
            emitted = true;
        }

        if (!emitted)
        {
            code.AppendLineAt(
                3,
                "throw new global::System.ArgumentException(\"This model has no keyed or dictionary collection members. Use FieldState for other members.\", nameof(collectionPath));"
            );
        }
        else
        {
            code.AppendLineAt(
                3,
                "throw new global::System.ArgumentException(\"The path does not address a root keyed or dictionary collection member. Use FieldState for other members.\", nameof(collectionPath));"
            );
        }

        code.AppendLineAt(2, "}");
    }
}
