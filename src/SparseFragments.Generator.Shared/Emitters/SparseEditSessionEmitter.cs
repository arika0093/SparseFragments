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

        code.AppendLineAt(0, accessibility + " " + declaration + modelName);
        code.AppendLineAt(0, "{");
        code.AppendLineAt(1, "/// <summary>A typed edit session for this model.</summary>");
        var sessionBases = sessionInterface + "<" + modelType + ", " + modelType + ".ChangeSet>";
        if (modelAccessorInterfaceMetadataName is not null)
        {
            sessionBases +=
                ", global::" + modelAccessorInterfaceMetadataName + "<" + modelType + ">";
        }
        code.AppendLineAt(1, "public sealed class EditSession : " + sessionBases);
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
                + ".__SparseRetargetTemps(live, submittedAfter, persisted, map!, pending), out error);"
        );
        // Raw model access bypasses the change cache, so it is exposed only
        // through ISparseEditSession<TModel>; framework code uses GetModelForFrameworkAccess().
        code.AppendLineAt(
            2,
            modelType + " " + sessionInterface + "<" + modelType + ">.Model => _session.Model;"
        );
        AppendForkMergeMembers(code, core, conflictType);
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
        string conflictType
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
        code.AppendLineAt(2, "public EditSession Fork() => new EditSession(_session.Fork());");
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
            "public bool TryMergeFrom(EditSession fork, out EditSession? merged, out global::System.Collections.Generic.IReadOnlyList<"
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
            "if (_session.TryMergeFrom(fork._session, out var mergedCore, out conflicts) && mergedCore is not null) { merged = new EditSession(mergedCore); return true; }"
        );
        code.AppendLineAt(3, "merged = null;");
        code.AppendLineAt(3, "return false;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(2, "internal EditSession(" + core + " session) { _session = session; }");
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
