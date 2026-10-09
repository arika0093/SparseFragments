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

    public static void EmitCore(SourceProductionContext context, SparseGeneratorConfig config)
    {
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

        context.AddSource(
            sessionDialect.CoreHintName,
            SourceText.From(sources.Core, Encoding.UTF8)
        );
        context.AddSource(
            sessionDialect.CurrentCoreHintName,
            SourceText.From(sources.CurrentCore, Encoding.UTF8)
        );
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
        var sessionInterfaceMetadataName =
            config.EditSessionInterfaceMetadataName
            ?? throw new ArgumentException(
                "An edit-session interface metadata name is required.",
                nameof(config)
            );
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
        code.AppendLineAt(
            1,
            "public sealed class EditSession : "
                + sessionInterface
                + "<"
                + modelType
                + ", "
                + modelType
                + ".ChangeSet>"
        );
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
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Advanced)]"
        );
        code.AppendLineAt(2, "public " + modelType + " Model => _session.Model;");
        code.AppendLineAt(
            2,
            "public " + modelType + "." + observable + " Observable => _session.Observable;"
        );
        code.AppendLineAt(
            2,
            "public " + modelType + "." + readOnlyView + " Current => _session.Current;"
        );
        if (
            config.DescriptorDialect is { } descriptorDialect
            && config.EffectiveEmissionFeatures.EmitObservable
        )
        {
            // Descriptors are live views: every getter/setter delegate reads the
            // current observable state, so the root set is cached per session
            // instead of reallocating the whole graph on each access.
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
                    + " Descriptors => __descriptors ?? (__descriptors = Observable."
                    + SparseObservableDescriptorEmitter.AccessorName(modelType)
                    + "(global::System.String.Empty));"
            );
        }
        code.AppendLineAt(2, "public bool HasChanges => _session.HasChanges;");
        code.AppendLineAt(
            2,
            "public event global::System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add => _session.PropertyChanged += value; remove => _session.PropertyChanged -= value; }"
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
            .ToString();
}
