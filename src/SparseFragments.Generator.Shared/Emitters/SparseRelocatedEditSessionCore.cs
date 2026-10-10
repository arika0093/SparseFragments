using System;
using System.Collections.Immutable;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits the relocated per-model edit-session class inside its container.</summary>
/// <remarks>
/// Mirrors <see cref="SparseEditSessionEmitter"/> but references the relocated
/// observable and read-only-view types directly instead of <c>Model.X</c>.
/// </remarks>
internal static class SparseRelocatedEditSessionCore
{
    /// <summary>Appends the relocated edit-session class.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="model">Owning model.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="observableRef">Qualified observable reference.</param>
    /// <param name="readOnlyRef">Qualified read-only-view reference.</param>
    public static void Append(
        SharedIndentedBuilder code,
        SparseModelInfo model,
        ImmutableArray<SparseMemberModel> members,
        SparseGeneratorConfig config,
        string observableRef,
        string readOnlyRef
    )
    {
        var adapterViolations = SparseEditSessionAdapterContract.ValidateAdapterFeatures(
            config.EffectiveEmissionFeatures
        );
        if (adapterViolations.Length != 0)
        {
            throw new ArgumentException(
                "Invalid edit-session adapter plan for '"
                    + model.ModelTypeName
                    + "': "
                    + string.Join(" ", adapterViolations),
                nameof(config)
            );
        }

        var modelType = model.ModelTypeName;
        var sessionInterfaceMetadataName =
            config.EditSessionInterfaceMetadataName
            ?? throw new ArgumentException(
                "An edit-session interface metadata name is required.",
                nameof(config)
            );
        var modelAccessorInterfaceMetadataName =
            config.EditSessionModelAccessorInterfaceMetadataName;
        var sessionDialect =
            config.EditSessionDialect
            ?? throw new ArgumentException("An edit-session dialect is required.", nameof(config));
        var patchDialect =
            config.PatchDialect
            ?? throw new ArgumentException("A patch dialect is required.", nameof(config));
        var sessionInterface = "global::" + sessionInterfaceMetadataName;
        var core =
            "global::"
            + sessionDialect.Namespace
            + ".EditSessionCore<"
            + modelType
            + ", "
            + modelType
            + ".Fragment, "
            + modelType
            + ".Patch, "
            + modelType
            + ".ChangeSet, "
            + observableRef
            + ", "
            + readOnlyRef
            + ">";
        var configuration =
            "global::"
            + sessionDialect.Namespace
            + ".EditSessionCoreConfiguration<"
            + modelType
            + ", "
            + modelType
            + ".Fragment, "
            + modelType
            + ".Patch, "
            + modelType
            + ".ChangeSet, "
            + observableRef
            + ", "
            + readOnlyRef
            + ">";
        var canWriteInPlace = members.All(static member =>
            !member.Property.IsReadOnly && !member.Property.IsInitOnly
        );
        var tryApply = canWriteInPlace
            ? "static (changes, current) => changes.TryApplyTo(current, out var updated, out var conflicts) ? (updated, null) : (null, conflicts)"
            : "static (changes, current) => { var candidate = "
                + modelType
                + ".Fragment.From(current).ToModel(); if (changes.TryApplyInPlace(candidate, out var conflicts)) return (candidate, null); return (null, conflicts); }";
        var writeModel = canWriteInPlace
            ? "static (current, updated) => "
                + modelType
                + ".Fragment.From(updated).WriteTo(current)"
            : "static (current, updated) => "
                + modelType
                + ".Fragment.From(updated).__SparseWriteWritableTo(current)";
        var conflictType = patchDialect.ConflictType;
        var rebaseResultType = patchDialect.RebaseResult(modelType + ".ChangeSet");

        code.AppendLineAt(1, "/// <summary>A typed edit session for this model.</summary>");
        var sessionBases = sessionInterface + "<" + modelType + ", " + modelType + ".ChangeSet>";
        if (modelAccessorInterfaceMetadataName is not null)
        {
            sessionBases +=
                ", global::" + modelAccessorInterfaceMetadataName + "<" + modelType + ">";
        }
        code.AppendLineAt(1, "public sealed class EditSession : " + sessionBases);
        code.AppendLineAt(1, "{");
        code.AppendLineAt(
            2,
            "private static readonly "
                + configuration
                + " __configuration = new "
                + configuration
                + " { FromModel = "
                + modelType
                + ".Fragment.From, Between = "
                + modelType
                + ".ChangeSet.Between, ToPatch = static changes => changes.ToPatch(), IsEmpty = static changes => changes.IsEmpty, AdvanceBaseline = static (changes, baseline) => changes.ApplyToBaseline(baseline), ToObservable = (current, changed, rawModelAccess) => new "
                + observableRef
                + "(current, changed, rawModelAccess), ToCurrent = static current => new "
                + readOnlyRef
                + "(current), TryApplyTo = "
                + tryApply
                + ", WriteModel = "
                + writeModel
                + ", Invert = static changes => changes.Invert(), Rebase = static (changes, server) => changes.RebaseOnto(server), EnumerateChangedPaths = static changes => changes.EnumerateChangedPaths(), RefreshObservable = static observable => observable.__SparseRefresh(), BaselineToModel = static fragment => fragment.ToModel() };"
        );
        code.AppendLineAt(2, "private readonly " + core + " _session;");
        code.AppendLineAt(
            2,
            "internal EditSession("
                + modelType
                + " model, global::System.Action? onChanged) : this(model, model, onChanged) { }"
        );
        code.AppendLineAt(
            2,
            "internal EditSession("
                + modelType
                + " baseline, "
                + modelType
                + " current, global::System.Action? onChanged) { _session = "
                + core
                + ".Create(baseline, current, __configuration, onChanged); }"
        );
        // Raw model access bypasses the change cache, so it is exposed only
        // through ISparseEditSession<TModel>; framework code uses GetModelForFrameworkAccess().
        code.AppendLineAt(
            2,
            modelType + " " + sessionInterface + "<" + modelType + ">.Model => _session.Model;"
        );
        if (modelAccessorInterfaceMetadataName is not null)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Gets the live model for trusted framework use without affecting the change cache.</summary>"
            );
            code.AppendLineAt(
                2,
                "public "
                    + modelType
                    + " GetModelForFrameworkAccess() => _session.GetModelForFrameworkAccess();"
            );
        }
        code.AppendLineAt(2, "public " + observableRef + " Observable => _session.Observable;");
        code.AppendLineAt(2, "public " + readOnlyRef + " Current => _session.Current;");
        if (
            config.DescriptorDialect is { } descriptorDialect
            && config.EffectiveEmissionFeatures.EmitObservable
        )
        {
            code.AppendLineAt(
                2,
                "private " + descriptorDialect.DescriptorSetInterface + "? __descriptors;"
            );
            code.AppendLineAt(
                2,
                "/// <summary>Gets descriptors bound to this session's observable model.</summary>"
            );
            code.AppendLineAt(
                2,
                "public "
                    + descriptorDialect.DescriptorSetInterface
                    + " Descriptors => __descriptors ?? (__descriptors = DescriptorFactory.Create(Observable, global::System.String.Empty));"
            );
        }
        code.AppendLineAt(2, "public bool HasChanges => _session.HasChanges;");
        code.AppendLineAt(
            2,
            "/// <summary>Occurs when the session state or current view changes.</summary>"
        );
        code.AppendLineAt(
            2,
            "public event global::System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add => _session.PropertyChanged += value; remove => _session.PropertyChanged -= value; }"
        );
        code.AppendLineAt(
            2,
            "/// <summary>Occurs when the session observes a committed transition.</summary>"
        );
        code.AppendLineAt(
            2,
            "public event global::System.Action<"
                + modelType
                + ".ChangeSet>? TransitionObserved { add => _session.TransitionObserved += value; remove => _session.TransitionObserved -= value; }"
        );
        code.AppendLineAt(
            2,
            "/// <summary>Runs observable edits as one transition notification, including nested batches.</summary>"
        );
        code.AppendLineAt(2, "/// <param name=\"edit\">The observable edits to run.</param>");
        code.AppendLineAt(
            2,
            "public void BatchEdit(global::System.Action edit) => _session.BatchEdit(edit);"
        );
        code.AppendLineAt(
            2,
            "public " + modelType + ".ChangeSet CreateChangeSet() => _session.CreateChangeSet();"
        );
        code.AppendLineAt(
            2,
            "public " + modelType + ".Patch CreatePatch() => _session.CreatePatch();"
        );
        code.AppendLineAt(
            2,
            "public global::System.Collections.Generic.IReadOnlyList<string> EnumerateChangedPaths() => _session.EnumerateChangedPaths();"
        );
        code.AppendLineAt(
            2,
            "public bool TryApplyInPlace("
                + modelType
                + ".ChangeSet changes, out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts) => _session.TryApplyInPlace(changes, out conflicts);"
        );
        code.AppendLineAt(
            2,
            "public void ApplyInPlace("
                + modelType
                + ".ChangeSet changes) => _session.ApplyInPlace(changes);"
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
            "public "
                + rebaseResultType
                + " Reload("
                + modelType
                + " serverState) => _session.Reload(serverState);"
        );
        code.AppendLineAt(2, "public void AcceptChanges() => _session.AcceptChanges();");
        code.AppendLineAt(
            2,
            "public void AcceptChanges("
                + modelType
                + ".ChangeSet changes) => _session.AcceptChanges(changes);"
        );
        code.AppendLineAt(1, "}");
    }
}
