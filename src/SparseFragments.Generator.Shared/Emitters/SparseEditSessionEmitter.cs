using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SparseFragments.Generator.Shared;

internal static class SparseEditSessionEmitter
{
    private const string CoreResourceName =
        "SparseFragments.Generator.Shared.Sessions.SparseEditSessionCore.template";
    private const string CurrentCoreResourceName =
        "SparseFragments.Generator.Shared.Sessions.SparseEditSessionWithCurrentCore.template";
    private static readonly Lazy<string> CoreTemplate = new(() => ReadTemplate(CoreResourceName));
    private static readonly Lazy<string> CurrentCoreTemplate = new(() =>
        ReadTemplate(CurrentCoreResourceName)
    );

    /// <summary>Emits the compilation-scoped reusable session cores once.</summary>
    /// <remarks>
    /// Generated-Once seam (#184): capabilities are de-duplicated per
    /// compilation and prerequisites are validated before emission. The
    /// capability aggregation plane (#178) and ownership boundary (#177) will
    /// call <see cref="SparseEditSessionCapabilities"/> at merge time; this
    /// entry point keeps the same deterministic dialect hint names so output
    /// stays byte-identical.
    /// </remarks>
    /// <param name="context">Generator output context.</param>
    /// <param name="config">Owning generator configuration.</param>
    public static void EmitCore(SourceProductionContext context, SparseGeneratorConfig config)
    {
        // Merge-time seam (#177/#178): capability aggregation moves to the
        // shared Generated-Once pipeline; per-model callers keep requesting
        // the full core pair until then.
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
        var openDraftStateInterfaceMetadataName = config.OpenDraftStateInterfaceMetadataName
            is { Length: > 0 } openDraftInterface
            ? openDraftInterface
            : null;
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
        var hasHistory = SparseEditSessionHistoryEmitter.TryGetHistoryTypes(
            config,
            out var historyType,
            out var historyOptionsType,
            out var historyHooksType
        );
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
        if (openDraftStateInterfaceMetadataName is not null)
        {
            sessionBases += ", global::" + openDraftStateInterfaceMetadataName;
        }
        // Sessions already raise PropertyChanged with the standard signature; declaring the
        // interface lets framework-neutral subscribers (such as the Blazor session scope)
        // attach without reflection or a ChangeSet-typed event.
        sessionBases += ", global::System.ComponentModel.INotifyPropertyChanged";
        code.AppendLineAt(1, "public sealed class " + sessionSimple + " : " + sessionBases);
        code.AppendLineAt(1, "{");
        // Public surface first; internal constructors and private state follow.
        code.AppendLineAt(2, "/// <summary>Gets the bindable proxy over the live model.</summary>");
        code.AppendLineAt(
            2,
            "public " + modelType + "." + observable + " Observable => _session.Observable;"
        );
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
            "/// <summary>Gets a value indicating whether the session has pending changes.</summary>"
        );
        code.AppendLineAt(2, "public bool HasChanges => _session.HasChanges;");
        if (openDraftStateInterfaceMetadataName is not null)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Gets whether this session has any open child drafts.</summary>"
            );
            code.AppendLineAt(2, "public bool HasOpenDrafts => __sparse_open_draft_count > 0;");
        }
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
            "/// <summary>Creates a change set describing the pending changes.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <returns>The pending change set; empty when there are no changes.</returns>"
        );
        code.AppendLineAt(
            2,
            "public " + modelType + ".ChangeSet CreateChangeSet() => _session.CreateChangeSet();"
        );
        code.AppendLineAt(
            2,
            "/// <summary>Creates a patch describing the pending changes.</summary>"
        );
        code.AppendLineAt(2, "/// <returns>The pending patch.</returns>");
        code.AppendLineAt(
            2,
            "public " + modelType + ".Patch CreatePatch() => _session.CreatePatch();"
        );
        code.AppendLineAt(2, "/// <summary>Enumerates the changed member paths.</summary>");
        code.AppendLineAt(2, "/// <returns>The changed paths in generated member order.</returns>");
        code.AppendLineAt(
            2,
            "public global::System.Collections.Generic.IReadOnlyList<"
                + SparseFragmentPatchEmitter.GetPathType(patchDialect)
                + "> EnumerateChangedPaths() => _session.EnumerateChangedPaths();"
        );
        // Path-first semantic state inspection (issue #250): live field and
        // root-collection queries over the retained baseline. Gated on the
        // state contracts so products without the runtime state types keep
        // byte-identical output.
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
            hasHistory,
            historyType,
            historyOptionsType,
            historyHooksType
        );
        // Raw model access bypasses the change cache, so it is exposed only
        // through ISparseEditSession<TModel>; framework code uses GetModelForFrameworkAccess().
        code.AppendLineAt(
            2,
            modelType + " " + sessionInterface + "<" + modelType + ">.Model => _session.Model;"
        );
        AppendScopeMembers(
            code,
            modelType,
            sessionSimple,
            sessionInterfaceMetadataName,
            openDraftStateInterfaceMetadataName,
            patchDialect,
            config.EffectiveEmissionFeatures.EmitObservable ? config.DescriptorDialect : null
        );
        AppendForkMergeMembers(code, core, conflictType, sessionSimple);
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
        SparseEditSessionHistoryEmitter.AppendHistoryFields(
            code,
            modelType,
            hasHistory,
            historyType
        );
        if (
            config.DescriptorDialect is { } descriptorsDialect
            && config.EffectiveEmissionFeatures.EmitObservable
        )
        {
            // Descriptors are live views: every getter/setter delegate reads the
            // current observable state, so the root set is cached per session
            // instead of reallocating the whole graph on each access.
            code.AppendLineAt(
                2,
                "private " + descriptorsDialect.DescriptorSetInterface + "? __descriptors;"
            );
        }
        code.AppendLineAt(2, "private int __sparse_open_draft_count;");
        code.AppendLineAt(
            2,
            "private void __SparseRegisterOpenDraft() => __sparse_open_draft_count++;"
        );
        code.AppendLineAt(
            2,
            "private void __SparseUnregisterOpenDraft() => __sparse_open_draft_count--;"
        );
        code.AppendLineAt(1, "}");
        code.AppendLineAt(0, "}");
        code.AppendLine();
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

    /// <summary>Appends path-addressed child edit scopes when descriptors are configured.</summary>
    internal static void AppendScopeMembers(
        SharedIndentedBuilder code,
        string modelType,
        string sessionSimple,
        string sessionInterfaceMetadataName,
        string? openDraftStateInterfaceMetadataName,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect,
        SparseDescriptorDialect? descriptorDialect
    )
    {
        if (descriptorDialect is null || patchDialect.TypedPathInterfaceType is not { Length: > 0 })
        {
            return;
        }

        var pathType = SparseFragmentPatchEmitter.GetPathType(patchDialect);
        var sessionContract = "global::" + sessionInterfaceMetadataName + "<TChild>";
        var openDraftStateContract = openDraftStateInterfaceMetadataName is null
            ? string.Empty
            : ", global::" + openDraftStateInterfaceMetadataName;
        var typedPathInterface =
            patchDialect.TypedPathInterfaceType + "<" + modelType + ", TChild>";
        var conflictType = patchDialect.ConflictType;

        code.AppendLineAt(
            2,
            "/// <summary>Identifies the lifecycle state of an edit scope.</summary>"
        );
        code.AppendLineAt(2, "public enum ScopeState");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "/// <summary>The scope can still be edited or committed.</summary>");
        code.AppendLineAt(3, "Open,");
        code.AppendLineAt(
            3,
            "/// <summary>The draft was committed to the parent session.</summary>"
        );
        code.AppendLineAt(3, "Committed,");
        code.AppendLineAt(3, "/// <summary>The draft was discarded.</summary>");
        code.AppendLineAt(3, "Canceled,");
        code.AppendLineAt(
            3,
            "/// <summary>The target no longer exists in the parent session.</summary>"
        );
        code.AppendLineAt(3, "TargetMissing,");
        code.AppendLineAt(
            3,
            "/// <summary>The parent scope was canceled, committed, or left its edit lineage.</summary>"
        );
        code.AppendLineAt(3, "ParentInvalidated,");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Opens an isolated edit scope for a model value addressed by a typed path.</summary>"
        );
        code.AppendLineAt(2, "/// <param name=\"path\">The path to the model value.</param>");
        code.AppendLineAt(
            2,
            "/// <returns>A scope containing an independent draft of the addressed model value.</returns>"
        );
        code.AppendLineAt(
            2,
            "/// <exception cref=\"global::System.ArgumentNullException\">The path is null.</exception>"
        );
        code.AppendLineAt(
            2,
            "/// <exception cref=\"global::System.ArgumentException\">The path does not resolve to a model value of the requested type.</exception>"
        );
        code.AppendLineAt(
            2,
            "/// <exception cref=\"global::System.ArgumentException\">The path does not address a generated model value.</exception>"
        );
        code.AppendLineAt(
            2,
            "public Scope<TChild> OpenScope<TChild>("
                + typedPathInterface
                + " path) where TChild : class"
        );
        code.AppendLineAt(2, "{");
        // Validate cheap preconditions before Fork: cloning the whole root
        // only to throw on an invalid path wastes the snapshot and the deep
        // clone. The fork snapshots the same state, so resolving here and
        // re-resolving on the fork cannot disagree; the second check stays as
        // a backstop that also produces the fork-owned target instance.
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
            "return new Scope<TChild>(this, fork, path.UntypedPath, path.WriteModelValueInPlace, target, this, null, null);"
        );
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>An isolated model draft that can be committed or discarded.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <typeparam name=\"TChild\">The addressed model type.</typeparam>"
        );
        code.AppendLineAt(
            2,
            "public sealed class Scope<TChild> : "
                + sessionContract
                + openDraftStateContract
                + ", global::System.IDisposable where TChild : class"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "/// <summary>Gets the editable child model.</summary>");
        code.AppendLineAt(3, "public TChild Model => _model;");
        code.AppendLineAt(3, "/// <summary>Gets the writable draft model.</summary>");
        code.AppendLineAt(3, "public TChild Observable => _model;");
        code.AppendLineAt(3, "/// <summary>Gets the current draft model.</summary>");
        code.AppendLineAt(3, "public TChild Current => _model;");
        if (openDraftStateInterfaceMetadataName is not null)
        {
            code.AppendLineAt(
                3,
                "/// <summary>Gets whether this scope contains any open nested drafts.</summary>"
            );
            code.AppendLineAt(3, "public bool HasOpenDrafts => _nestedScopes is { Count: > 0 };");
        }
        code.AppendLineAt(
            3,
            "/// <summary>Gets whether the draft differs from its state when the scope opened.</summary>"
        );
        code.AppendLineAt(
            3,
            "public bool HasChanges => IsOpenInAncestry && !_fork.CreateChangeSet().IsEmpty;"
        );
        code.AppendLineAt(3, "/// <summary>Gets the target path.</summary>");
        code.AppendLineAt(3, "public " + pathType + " Path => _path;");
        code.AppendLineAt(3, "/// <summary>Gets the scope lifecycle state.</summary>");
        code.AppendLineAt(3, "public ScopeState State => _state;");
        code.AppendLineAt(
            3,
            "/// <summary>Attempts to merge the draft into the parent's current unsaved state.</summary>"
        );
        code.AppendLineAt(
            3,
            "/// <param name=\"conflicts\">Structured conflicts when the draft cannot be committed.</param>"
        );
        code.AppendLineAt(
            3,
            "/// <returns><see langword=\"true\"/> when the draft was committed; otherwise <see langword=\"false\"/>.</returns>"
        );
        code.AppendLineAt(
            3,
            "/// <exception cref=\"global::System.InvalidOperationException\">The scope is closed, its parent is invalid, or the parent and draft belong to unrelated lineages.</exception>"
        );
        code.AppendLineAt(
            3,
            "public bool TryCommit(out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (_state != ScopeState.Open) throw new global::System.InvalidOperationException(\"The edit scope is no longer open.\");"
        );
        code.AppendLineAt(
            4,
            "if (!_parent._session.IsInSameLineage(_fork._session) || (_parentIsOpen is not null && !_parentIsOpen())) { Close(ScopeState.ParentInvalidated); throw new global::System.InvalidOperationException(\"The parent edit scope is no longer open or belongs to an unrelated edit lineage.\"); }"
        );
        code.AppendLineAt(
            4,
            "if (_parent.Descriptors.ResolveModel(_path) is not TChild) { Close(ScopeState.TargetMissing); conflicts = null; return false; }"
        );
        code.AppendLineAt(
            4,
            "if (_fork.CreateChangeSet().IsEmpty) { Close(ScopeState.Committed); conflicts = null; return true; }"
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
            "if (!rebased.Rebased.IsEmpty) { var candidate = _parent.Fork(); if (!candidate.TryApplyInPlace(rebased.Rebased, out conflicts)) return false; if (_parent.Descriptors.ResolveModel(_path) is not TChild currentTarget || candidate.Descriptors.ResolveModel(_path) is not TChild updatedTarget) { Close(ScopeState.TargetMissing); conflicts = null; return false; } try { _writeModelValue(currentTarget, updatedTarget); } finally { _parent._session.RefreshAfterExternalMutation(); } }"
        );
        code.AppendLineAt(4, "conflicts = null;");
        code.AppendLineAt(4, "Close(ScopeState.Committed);");
        code.AppendLineAt(4, "return true;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "/// <summary>Commits the child draft into its parent session.</summary>"
        );
        code.AppendLineAt(
            3,
            "/// <exception cref=\"global::System.InvalidOperationException\">The draft conflicts with its parent, its target is missing, or its parent scope is invalid.</exception>"
        );
        code.AppendLineAt(
            3,
            "public void AcceptChanges() { if (!TryCommit(out var conflicts)) throw new global::System.InvalidOperationException(conflicts is null ? \"The edit scope target is no longer available.\" : \"The edit scope conflicts with changes in its parent session.\"); }"
        );
        code.AppendLineAt(
            3,
            "/// <summary>Opens a nested scope for a model value within this child draft.</summary>"
        );
        code.AppendLineAt(
            3,
            "/// <param name=\"path\">The path to the nested model value.</param>"
        );
        code.AppendLineAt(
            3,
            "/// <returns>A nested scope that commits into this scope's draft.</returns>"
        );
        code.AppendLineAt(
            3,
            "public Scope<TNested> OpenScope<TNested>("
                + patchDialect.TypedPathInterfaceType
                + "<TChild, TNested> path) where TNested : class"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (!IsOpenInAncestry) throw new global::System.InvalidOperationException(\"The parent edit scope is no longer open.\");"
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
            "return new Scope<TNested>(_fork, fork, nestedPath, path.WriteModelValueInPlace, target, _scopeOwner, () => IsOpenInAncestry, RegisterNestedScope);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "/// <summary>Discards the draft without changing the parent session.</summary>"
        );
        code.AppendLineAt(3, "public void Cancel() => Close(ScopeState.Canceled);");
        code.AppendLineAt(3, "/// <inheritdoc />");
        code.AppendLineAt(3, "public void Dispose() => Close(ScopeState.Canceled);");
        code.AppendLineAt(
            3,
            "internal Scope("
                + sessionSimple
                + " parent, "
                + sessionSimple
                + " fork, "
                + pathType
                + " path, global::System.Action<TChild, TChild> writeModelValue, TChild model, "
                + sessionSimple
                + " scopeOwner, global::System.Func<bool>? parentIsOpen, global::System.Func<global::System.Action, global::System.Action>? registerNestedScope) { _parent = parent; _fork = fork; _path = path; _writeModelValue = writeModelValue; _model = model; _scopeOwner = scopeOwner; _parentIsOpen = parentIsOpen; _removeFromParent = registerNestedScope?.Invoke(InvalidateFromParent); _scopeOwner.__SparseRegisterOpenDraft(); _state = ScopeState.Open; }"
        );
        code.AppendLineAt(3, "private readonly " + sessionSimple + " _parent;");
        code.AppendLineAt(3, "private readonly " + sessionSimple + " _fork;");
        code.AppendLineAt(3, "private readonly " + pathType + " _path;");
        code.AppendLineAt(
            3,
            "private readonly global::System.Action<TChild, TChild> _writeModelValue;"
        );
        code.AppendLineAt(3, "private readonly global::System.Func<bool>? _parentIsOpen;");
        code.AppendLineAt(3, "private readonly TChild _model;");
        code.AppendLineAt(3, "private readonly " + sessionSimple + " _scopeOwner;");
        code.AppendLineAt(
            3,
            "private global::System.Collections.Generic.List<global::System.Action>? _nestedScopes;"
        );
        code.AppendLineAt(3, "private global::System.Action? _removeFromParent;");
        code.AppendLineAt(3, "private ScopeState _state;");
        code.AppendLineAt(
            3,
            "private bool IsOpenInAncestry => _state == ScopeState.Open && (_parentIsOpen is null || _parentIsOpen());"
        );
        code.AppendLineAt(
            3,
            "private global::System.Action RegisterNestedScope(global::System.Action close) { var nested = _nestedScopes ??= new global::System.Collections.Generic.List<global::System.Action>(); nested.Add(close); return () => nested.Remove(close); }"
        );
        code.AppendLineAt(
            3,
            "private void InvalidateFromParent() => Close(ScopeState.ParentInvalidated);"
        );
        code.AppendLineAt(
            3,
            "private void Close(ScopeState state) { if (_state != ScopeState.Open) return; _state = state; var nested = _nestedScopes; while (nested is { Count: > 0 }) nested[0](); _nestedScopes = null; _removeFromParent?.Invoke(); _removeFromParent = null; _scopeOwner.__SparseUnregisterOpenDraft(); }"
        );
        code.AppendLineAt(2, "}");
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
