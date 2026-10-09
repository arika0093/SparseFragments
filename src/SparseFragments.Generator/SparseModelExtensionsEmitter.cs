using System.Collections.Immutable;
using System.Threading;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Generator;

/// <summary>Emits framework-neutral model extension APIs in a stable top-level container.</summary>
internal static class SparseModelExtensionsEmitter
{
    public static string ContainerName(SparseModelInfo model, CancellationToken cancellationToken)
    {
        var fullyQualifiedName = model.ModelTypeName;
        return SparseNaming.Sanitize(model.Name + "Extensions", cancellationToken)
            + "_"
            + SparseNaming.GetStableTypeHash(fullyQualifiedName, cancellationToken);
    }

    public static void Append(
        SharedIndentedBuilder code,
        SparseModelInfo model,
        ImmutableArray<SparseMemberModel> members,
        string sessionInterfaceMetadataName
    )
    {
        var cancellationToken = code.CancellationToken;
        var modelType = model.ModelTypeName;
        var extensionClass = ContainerName(model, cancellationToken);
        var observable = SparseObservableEmitter.ObservableTypeName(members);
        var accessibility = model.IsPublic ? "public" : "internal";
        var readOnlyView = SparseReadOnlyViewEmitter.ReadOnlyViewTypeName(members);

        code.AppendLine();
        if (!model.IsStruct)
        {
            AppendEditSessionType(
                code,
                model,
                modelType,
                accessibility,
                observable,
                readOnlyView,
                members,
                sessionInterfaceMetadataName
            );
        }

        code.AppendLineAt(0, accessibility + " static partial class " + extensionClass);
        code.AppendLineAt(0, "{");
        code.AppendLineAt(
            1,
            "/// <summary>Derives the baseline-aware change set from this baseline to the supplied current model.</summary>"
        );
        code.AppendLineAt(
            1,
            accessibility
                + " static "
                + modelType
                + ".ChangeSet CreateChangeSet(this "
                + modelType
                + " baseline, "
                + modelType
                + " current) => "
                + modelType
                + ".ChangeSet.Between(baseline, current);"
        );

        if (!model.IsStruct)
        {
            var editSession = modelType + ".EditSession";
            code.AppendLineAt(
                1,
                "/// <summary>Creates a framework-neutral edit session using this model as both the baseline source and live current value.</summary>"
            );
            code.AppendLineAt(
                1,
                accessibility
                    + " static "
                    + editSession
                    + " CreateEditSession(this "
                    + modelType
                    + " model, global::System.Action? onChanged = null) => new "
                    + editSession
                    + "(model, onChanged);"
            );
            code.AppendLineAt(
                1,
                "/// <summary>Creates a framework-neutral edit session that edits the supplied current model against a separate baseline snapshot.</summary>"
            );
            code.AppendLineAt(
                1,
                accessibility
                    + " static "
                    + editSession
                    + " CreateEditSession(this "
                    + modelType
                    + " baseline, "
                    + modelType
                    + " current, global::System.Action? onChanged = null) => new "
                    + editSession
                    + "(baseline, current, onChanged);"
            );

            code.AppendLineAt(
                1,
                "/// <summary>Maps an optional model to its typed observable proxy while preserving missing and present-null states.</summary>"
            );
            code.AppendLineAt(
                1,
                accessibility
                    + " static global::SparseFragments.Optional<"
                    + modelType
                    + "."
                    + observable
                    + "?> ToObservable(this global::SparseFragments.Optional<"
                    + modelType
                    + "?> model, global::System.Action? onChanged = null)"
            );
            code.AppendLineAt(1, "{");
            code.AppendLineAt(
                2,
                "if (!model.IsPresent) return global::SparseFragments.Optional<"
                    + modelType
                    + "."
                    + observable
                    + "?>.Missing;"
            );
            code.AppendLineAt(2, "var value = model.Value;");
            code.AppendLineAt(
                2,
                "return global::SparseFragments.Optional<"
                    + modelType
                    + "."
                    + observable
                    + "?>.Present(value is null ? null : new "
                    + modelType
                    + "."
                    + observable
                    + "(value, onChanged));"
            );
            code.AppendLineAt(1, "}");
        }

        code.AppendLineAt(0, "}");
    }

    private static void AppendEditSessionType(
        SharedIndentedBuilder code,
        SparseModelInfo model,
        string modelType,
        string accessibility,
        string observable,
        string readOnlyView,
        ImmutableArray<SparseMemberModel> members,
        string sessionInterfaceMetadataName
    )
    {
        var modelName = SparseNaming.EscapeIdentifier(model.Name);
        var declaration = model.IsRecord ? "partial record " : "partial class ";
        var sessionInterface = "global::" + sessionInterfaceMetadataName;
        var core =
            "global::SparseFragments.__GeneratedSessionCore.EditSessionCore<"
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
            "global::SparseFragments.__GeneratedSessionCore.EditSessionCoreConfiguration<"
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
                + ", Invert = static changes => changes.Invert(), Rebase = static (changes, server) => changes.RebaseOnto(server), EnumerateChangedPaths = static changes => changes.EnumerateChangedPaths(), RefreshObservable = static observable => observable.__SparseRefresh() };"
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
                + ".ChangeSet changes, out global::System.Collections.Generic.IReadOnlyList<global::SparseFragments.SparseConflict>? conflicts) => _session.TryApplyInPlace(changes, out conflicts);"
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
            "public global::SparseFragments.RebaseResult<"
                + modelType
                + ".ChangeSet> Reload("
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
}
