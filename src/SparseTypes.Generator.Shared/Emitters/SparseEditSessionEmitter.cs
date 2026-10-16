using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SparseTypes.Generator.Shared;

internal static class SparseEditSessionEmitter
{
    private const string CoreResourceName =
        "SparseTypes.Generator.Shared.Sessions.SparseEditSessionCore.template";
    private const string CurrentCoreResourceName =
        "SparseTypes.Generator.Shared.Sessions.SparseEditSessionWithCurrentCore.template";
    private static readonly Lazy<string> CoreTemplate = new(() => ReadTemplate(CoreResourceName));
    private static readonly Lazy<string> CurrentCoreTemplate = new(() =>
        ReadTemplate(CurrentCoreResourceName)
    );

    internal static bool HasChildSessionSurface(
        SparseGeneratorConfig config,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect
    ) =>
        config.DescriptorDialect is not null
        && patchDialect.TypedPathInterfaceType is { Length: > 0 }
        && config.EditSessionWithChildrenInterfaceMetadataName is { Length: > 0 }
        && config.EditChildPathInterfaceMetadataName is { Length: > 0 }
        && config.EditChildSessionInterfaceMetadataName is { Length: > 0 }
        && config.EditChildSessionImplementationType is { Length: > 0 }
        && config.EditChildSessionStatusType is { Length: > 0 }
        && config.EditSessionWithChildStatusInterfaceMetadataName is { Length: > 0 }
        && patchDialect.ChildConflictPathPrefixExpression is not null;

    /// <summary>Emits the compilation-scoped reusable session cores once.</summary>
    /// <remarks>Generated-Once seam: capabilities are de-duplicated per compilation and prerequisites are validated before emission.</remarks>
    /// <param name="context">Generator output context.</param>
    /// <param name="config">Owning generator configuration.</param>
    public static void EmitCore(SourceProductionContext context, SparseGeneratorConfig config)
    {
        // Capability aggregation moves to the shared Generated-Once pipeline;
        // per-model callers keep requesting the full core pair until then.
        var capability = SparseEditSessionCapabilities.ForCompilation(
            hasSessionModels: true,
            needsCurrentView: true
        );
        var violations = SparseEditSessionCapabilities.ValidatePrerequisites(config, capability);
        if (violations.Length != 0)
        {
            throw new ArgumentException(
                "Invalid edit-session capability plan: " + string.Join(" ", violations),
                nameof(config)
            );
        }

        EmitCapability(context, config, capability);
    }

    /// <summary>Emits exactly the requested session-core subset.</summary>
    /// <param name="context">Generator output context.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="capability">De-duplicated capability set.</param>
    public static void EmitCapability(
        SourceProductionContext context,
        SparseGeneratorConfig config,
        SparseEditSessionCapability capability
    )
    {
        if (capability == SparseEditSessionCapability.None)
        {
            return;
        }

        var sessionDialect =
            config.EditSessionDialect
            ?? throw new ArgumentException("An edit-session dialect is required.", nameof(config));
        var runtimeDialect =
            config.RuntimeDialect
            ?? throw new ArgumentException("A runtime dialect is required.", nameof(config));
        var patchDialect =
            config.PatchDialect
            ?? throw new ArgumentException("A patch dialect is required.", nameof(config));
        var sources = RenderCoreSources(sessionDialect, runtimeDialect, patchDialect);

        if (SparseEditSessionCapabilities.NeedsCore(capability))
        {
            context.AddSource(
                sessionDialect.CoreHintName,
                SourceText.From(sources.Core, Encoding.UTF8)
            );
        }

        if (SparseEditSessionCapabilities.NeedsWithCurrent(capability))
        {
            context.AddSource(
                sessionDialect.CurrentCoreHintName,
                SourceText.From(sources.CurrentCore, Encoding.UTF8)
            );
        }
    }

    internal static (string Core, string CurrentCore) RenderCoreSources(
        SparseEditSessionDialect sessionDialect,
        SparseRuntimeDialect runtimeDialect,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect
    ) =>
        (
            RenderTemplate(CoreTemplate.Value, sessionDialect, runtimeDialect, patchDialect),
            RenderTemplate(CurrentCoreTemplate.Value, sessionDialect, runtimeDialect, patchDialect)
        );

    public static void AppendModelEditSession(
        SharedIndentedBuilder code,
        SparseModelInfo model,
        string modelType,
        string accessibility,
        string observable,
        string readOnlyView,
        System.Collections.Immutable.ImmutableArray<SparseMemberModel> members,
        SparseGeneratorConfig config
    )
    {
        // Diagnose adapter binding at generation time: the connection contract
        // needs Fragment/Patch/ChangeSet/Observable families up front.
        var adapterViolations = SparseEditSessionAdapterContract.ValidateAdapterFeatures(
            config.EffectiveEmissionFeatures
        );
        if (adapterViolations.Length != 0)
        {
            throw new ArgumentException(
                "Invalid edit-session adapter plan for '"
                    + modelType
                    + "': "
                    + string.Join(" ", adapterViolations),
                nameof(config)
            );
        }

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
        var modelName = SparseNaming.EscapeIdentifier(model.Name);
        var declaration = model.IsRecord ? "partial record " : "partial class ";
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
            + modelType
            + "."
            + observable
            + ", "
            + modelType
            + "."
            + readOnlyView
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
            + modelType
            + "."
            + observable
            + ", "
            + modelType
            + "."
            + readOnlyView
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

        code.AppendLineAt(0, accessibility + " " + declaration + modelName);
        code.AppendLineAt(0, "{");
        code.AppendLineAt(1, "/// <summary>A typed edit session for this model.</summary>");
        var sessionBases = sessionInterface + "<" + modelType + ", " + modelType + ".ChangeSet>";
        if (modelAccessorInterfaceMetadataName is not null)
        {
            sessionBases +=
                ", global::" + modelAccessorInterfaceMetadataName + "<" + modelType + ">";
        }
        if (HasChildSessionSurface(config, patchDialect))
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
        if (
            config.EditSessionWithBaselineRevisionInterfaceMetadataName is
            { Length: > 0 } baselineRevisionInterface
        )
        {
            sessionBases += ", global::" + baselineRevisionInterface;
        }
        sessionBases += SparseEditSessionPathsEmitter.GetPathsInterfaceBase(
            model,
            modelType,
            config,
            patchDialect,
            code.CancellationToken
        );
        sessionBases = SparseEditSessionCapabilityEmitter.AppendSessionCapabilityBases(
            sessionBases,
            modelType,
            config,
            patchDialect,
            historyBinding?.HasOwnedHistory == true
        );
        // Sessions already raise PropertyChanged with the standard signature.
        sessionBases += ", global::System.ComponentModel.INotifyPropertyChanged";
        code.AppendLineAt(1, "public sealed class " + sessionSimple + " : " + sessionBases);
        code.AppendLineAt(1, "{");
        // Public surface first; internal constructors and private state follow.
        code.AppendLineAt(2, "/// <summary>Gets the bindable proxy over the live model.</summary>");
        code.AppendLineAt(
            2,
            "public " + modelType + "." + observable + " Observable => _session.Observable;"
        );
        if (config.EditSessionWithBaselineRevisionInterfaceMetadataName is { Length: > 0 })
        {
            code.AppendLineAt(
                2,
                "/// <summary>Gets the revision of the retained baseline.</summary>"
            );
            code.AppendLineAt(2, "public long BaselineRevision => _session.BaselineRevision;");
        }
        code.AppendLineAt(2, "/// <summary>Gets the read-only view over the live model.</summary>");
        code.AppendLineAt(
            2,
            "public " + modelType + "." + readOnlyView + " Current => _session.Current;"
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
        if (
            config.DescriptorDialect is { } descriptorDialect
            && config.EffectiveEmissionFeatures.EmitObservable
        )
        {
            code.AppendLineAt(
                2,
                "/// <summary>Gets descriptors bound to this session's observable model.</summary>"
            );
            code.AppendLineAt(
                2,
                "public "
                    + descriptorDialect.DescriptorSetInterface
                    + " Descriptors => __descriptors ?? (__descriptors = Observable."
                    + SparseObservableDescriptorEmitter.AccessorName(modelType)
                    + "("
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
                    HasChildSessionSurface(config, patchDialect)
                        ? "this session or any child session"
                        : "the session"
                )
                + " has pending changes.</summary>"
        );
        code.AppendLineAt(
            2,
            "public bool HasChanges => _session.HasChanges"
                + (
                    HasChildSessionSurface(config, patchDialect)
                        ? " || HasPendingChildSessions"
                        : string.Empty
                )
                + ";"
        );
        if (HasChildSessionSurface(config, patchDialect))
        {
            code.AppendLineAt(
                2,
                "/// <summary>Gets whether this session has changes apart from its registered child sessions.</summary>"
            );
            code.AppendLineAt(2, "public bool HasOwnChanges => _session.HasChanges;");
            var childPathType = config.DescriptorDialect!.EffectivePathType(
                config.RuntimeDialect?.Namespace ?? patchDialect.RuntimeNamespace
            );
            code.AppendLineAt(
                2,
                "/// <summary>Resolves the model currently addressed by a child-session path.</summary>"
            );
            code.AppendLineAt(
                2,
                "public object? ResolveChildModel("
                    + childPathType
                    + " path) => Descriptors.ResolveModel(path);"
            );
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
        // Path-first semantic state inspection: live field and root-collection
        // queries over the retained baseline.
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
            patchDialect.ReconcileConflictExpression?.Invoke(modelType, "error"),
            HasChildSessionSurface(config, patchDialect)
        );
        // Raw model access bypasses the change cache.
        code.AppendLineAt(
            2,
            modelType + " " + sessionInterface + "<" + modelType + ">.Model => _session.Model;"
        );
        SparseEditSessionChildEmitter.AppendChildSessionApiMembers(
            code,
            modelType,
            sessionSimple,
            patchDialect,
            config.EffectiveEmissionFeatures.EmitObservable ? config.DescriptorDialect : null,
            config
        );
        SparseEditSessionChildEmitter.AppendForkMergeMembers(
            code,
            core,
            conflictType,
            sessionSimple
        );
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
        SparseEditSessionChildEmitter.AppendChildDraftMembers(
            code,
            modelType,
            sessionSimple,
            patchDialect,
            config.EffectiveEmissionFeatures.EmitObservable ? config.DescriptorDialect : null,
            config
        );
        if (HasChildSessionSurface(config, patchDialect))
        {
            // Private candidate projection trails every public member.
            SparseEditSessionChildEmitter.AppendCandidateBuilder(
                code,
                modelType,
                patchDialect.ConflictType,
                patchDialect
            );
        }
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
                + modelType
                + "."
                + observable
                + "(current, changed, rawModelAccess), ToCurrent = static current => new "
                + modelType
                + "."
                + readOnlyView
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
            // Descriptors are live views.
            code.AppendLineAt(
                2,
                "private " + descriptorsDialect.DescriptorSetInterface + "? __descriptors;"
            );
        }
        code.AppendLineAt(1, "}");
        code.AppendLineAt(0, "}");
        code.AppendLine();
    }

    private static string ReadTemplate(string resourceName)
    {
        var assembly = typeof(SparseEditSessionEmitter).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new InvalidOperationException(
                "Embedded edit-session source template is missing: "
                    + resourceName
                    + " in "
                    + assembly.FullName
                    + ". Available resources: "
                    + string.Join(", ", assembly.GetManifestResourceNames())
            );
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string RenderTemplate(
        string template,
        SparseEditSessionDialect sessionDialect,
        SparseRuntimeDialect runtimeDialect,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect
    ) =>
        new StringBuilder(template)
            .Replace("__SESSION_NAMESPACE__", sessionDialect.Namespace)
            .Replace("__OPTIONAL_TYPE__", runtimeDialect.OptionalType)
            .Replace("__CONFLICT_TYPE__", patchDialect.ConflictType)
            .Replace("__REBASE_RESULT_TYPE__", patchDialect.RebaseResult("TChangeSet"))
            .Replace("__PATH_TYPE__", SparseFragmentPatchEmitter.GetPathType(patchDialect))
            .ToString();
}
