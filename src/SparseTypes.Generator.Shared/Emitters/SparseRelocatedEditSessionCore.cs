using System;
using System.Collections.Immutable;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits the per-model edit-session class nested in the model.</summary>
/// <remarks>Mirrors <see cref="SparseEditSessionEmitter"/> but takes the model-nested observable and read-only-view references explicitly.</remarks>
internal static class SparseRelocatedEditSessionCore
{
    /// <summary>Appends the model-nested edit-session class.</summary>
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
        SparseEditSessionHistoryEmitter.TryGetHistoryBinding(config, out var historyBinding);
        // A same-named member pushes the session behind a Sparse prefix,
        // mirroring observable/view collision resolution.
        var sessionSimple = SparseGeneratedPlacement.ResolveUiTypeName(
            "EditSession",
            members.Select(static member => member.Property.Name)
        );

        code.AppendLineAt(1, "/// <summary>A typed edit session for this model.</summary>");
        var sessionBases = sessionInterface + "<" + modelType + ", " + modelType + ".ChangeSet>";
        if (modelAccessorInterfaceMetadataName is not null)
        {
            sessionBases +=
                ", global::" + modelAccessorInterfaceMetadataName + "<" + modelType + ">";
        }
        if (SparseEditSessionEmitter.HasChildSessionSurface(config, patchDialect))
        {
            sessionBases +=
                ", global::"
                + config.EditSessionWithChildStatusInterfaceMetadataName
                + "<"
                + modelType
                + ", "
                + patchDialect.ConflictType
                + ">";
        }
        if (
            config.EditSessionWithChangeSetInterfaceMetadataName is
            { Length: > 0 } changeSetInterface
        )
        {
            sessionBases +=
                ", global::"
                + changeSetInterface
                + "<"
                + modelType
                + ", "
                + modelType
                + ".ChangeSet>";
        }
        sessionBases += SparseEditSessionPathsEmitter.GetPathsInterfaceBase(
            model,
            modelType,
            config,
            patchDialect,
            code.CancellationToken
        );
        // Sessions already raise PropertyChanged with the standard signature.
        sessionBases += ", global::System.ComponentModel.INotifyPropertyChanged";
        code.AppendLineAt(1, "public sealed class " + sessionSimple + " : " + sessionBases);
        code.AppendLineAt(1, "{");
        // Public surface first; internal constructors and private state follow.
        code.AppendLineAt(2, "/// <summary>Gets the bindable proxy over the live model.</summary>");
        code.AppendLineAt(2, "public " + observableRef + " Observable => _session.Observable;");
        code.AppendLineAt(2, "/// <summary>Gets the read-only view over the live model.</summary>");
        code.AppendLineAt(2, "public " + readOnlyRef + " Current => _session.Current;");
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
        if (
            config.DescriptorDialect is { } descriptorDialect
            && config.EffectiveEmissionFeatures.EmitObservable
        )
        {
            var factoryRef = SparseGeneratedPlacement.GetDescriptorFactoryReference(
                model,
                config,
                code.CancellationToken
            );
            code.AppendLineAt(
                2,
                "/// <summary>Gets descriptors bound to this session's observable model.</summary>"
            );
            code.AppendLineAt(
                2,
                "public "
                    + descriptorDialect.DescriptorSetInterface
                    + " Descriptors => __descriptors ?? (__descriptors = "
                    + factoryRef
                    + ".Create(Observable, "
                    + descriptorDialect.EffectivePathType(
                        config.RuntimeDialect?.Namespace ?? patchDialect.RuntimeNamespace
                    )
                    + ".Root(typeof("
                    + modelType
                    + "))));"
            );
        }
        code.AppendLineAt(
            2,
            "/// <summary>Gets whether "
                + (
                    SparseEditSessionEmitter.HasChildSessionSurface(config, patchDialect)
                        ? "this session or any child session"
                        : "the session"
                )
                + " has pending changes.</summary>"
        );
        code.AppendLineAt(
            2,
            "public bool HasChanges => _session.HasChanges"
                + (
                    SparseEditSessionEmitter.HasChildSessionSurface(config, patchDialect)
                        ? " || HasPendingChildSessions"
                        : string.Empty
                )
                + ";"
        );
        if (SparseEditSessionEmitter.HasChildSessionSurface(config, patchDialect))
        {
            code.AppendLineAt(
                2,
                "/// <summary>Gets whether this session has changes apart from its registered child sessions.</summary>"
            );
            code.AppendLineAt(2, "public bool HasOwnChanges => _session.HasChanges;");
        }
        SparseEditSessionPathsEmitter.AppendPathsProperty(
            code,
            model,
            modelType,
            config,
            patchDialect
        );
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
        SparseEditSessionChangeSetEmitter.AppendMembers(code, modelType, config, patchDialect);
        code.AppendLineAt(
            2,
            "/// <summary>Creates a patch describing the pending changes.</summary>"
        );
        code.AppendLineAt(2, "/// <returns>The pending patch.</returns>");
        code.AppendLineAt(
            2,
            "public " + modelType + ".Patch CreatePatch() => CreateChangeSet().ToPatch();"
        );
        code.AppendLineAt(2, "/// <summary>Enumerates the changed member paths.</summary>");
        code.AppendLineAt(2, "/// <returns>The changed paths in generated member order.</returns>");
        code.AppendLineAt(
            2,
            "public global::System.Collections.Generic.IReadOnlyList<"
                + SparseFragmentPatchEmitter.GetPathType(patchDialect)
                + "> EnumerateChangedPaths() => CreateChangeSet().EnumerateChangedPaths();"
        );
        // Path-first semantic state inspection; shared with the
        // model-nested session placement so both surfaces stay identical.
        SparseEditSessionStateEmitter.AppendStateMembers(code, modelType, members, patchDialect);
        code.AppendLineAt(
            2,
            "/// <summary>Applies a change set to the current model in place when possible.</summary>"
        );
        code.AppendLineAt(2, "/// <param name=\"changes\">The change set to apply.</param>");
        code.AppendLineAt(
            2,
            "/// <param name=\"conflicts\">Structured conflicts when the change set cannot be applied in place.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <returns><see langword=\"true\"/> when the model was updated; otherwise <see langword=\"false\"/>.</returns>"
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
            "/// <summary>Applies a change set to the current model in place.</summary>"
        );
        code.AppendLineAt(2, "/// <param name=\"changes\">The change set to apply.</param>");
        code.AppendLineAt(
            2,
            "public void ApplyInPlace("
                + modelType
                + ".ChangeSet changes) => _session.ApplyInPlace(changes);"
        );
        SparseEditSessionHistoryEmitter.AppendLifecycleMembers(
            code,
            modelType,
            conflictType,
            rebaseResultType,
            historyBinding,
            patchDialect.ReconcileConflictExpression?.Invoke(modelType, "error")
        );
        // Raw model access bypasses the change cache.
        code.AppendLineAt(
            2,
            modelType + " " + sessionInterface + "<" + modelType + ">.Model => _session.Model;"
        );
        SparseEditSessionEmitter.AppendChildSessionApiMembers(
            code,
            modelType,
            patchDialect,
            config.EffectiveEmissionFeatures.EmitObservable ? config.DescriptorDialect : null,
            config
        );
        SparseEditSessionEmitter.AppendForkMergeMembers(code, core, conflictType, sessionSimple);
        code.AppendLineAt(
            2,
            "internal "
                + sessionSimple
                + "("
                + modelType
                + " model, global::System.Action? onChanged) : this(model, model, onChanged) { }"
        );
        code.AppendLineAt(
            2,
            "internal "
                + sessionSimple
                + "("
                + modelType
                + " baseline, "
                + modelType
                + " current, global::System.Action? onChanged) { _session = "
                + core
                + ".Create(baseline, current, __configuration, onChanged); }"
        );
        SparseEditSessionEmitter.AppendChildDraftMembers(
            code,
            modelType,
            sessionSimple,
            patchDialect,
            config.EffectiveEmissionFeatures.EmitObservable ? config.DescriptorDialect : null,
            config
        );
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
        SparseEditSessionPathsEmitter.AppendPathsField(
            code,
            model,
            modelType,
            config,
            patchDialect
        );
        SparseEditSessionHistoryEmitter.AppendHistoryFields(
            code,
            modelType,
            conflictType,
            historyBinding
        );
        if (
            config.DescriptorDialect is { } descriptorsDialect
            && config.EffectiveEmissionFeatures.EmitObservable
        )
        {
            code.AppendLineAt(
                2,
                "private " + descriptorsDialect.DescriptorSetInterface + "? __descriptors;"
            );
        }
        code.AppendLineAt(1, "}");
    }
}
