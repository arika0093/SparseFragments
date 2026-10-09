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
        ImmutableArray<SparseMemberModel> members
    )
    {
        var cancellationToken = code.CancellationToken;
        var modelType = model.ModelTypeName;
        var extensionClass = ContainerName(model, cancellationToken);
        var observable = SparseObservableEmitter.ObservableTypeName(members);
        var accessibility = model.IsPublic ? "public" : "internal";
        var readOnlyView = SparseReadOnlyViewEmitter.ReadOnlyViewTypeName(members);

        code.AppendLine();
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
            var session =
                "global::SparseFragments.SparseEditSession<"
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
                "global::SparseFragments.SparseEditSessionConfiguration<"
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
            code.AppendLineAt(
                1,
                "/// <summary>Creates a framework-neutral edit session using this model as both the baseline source and live current value.</summary>"
            );
            code.AppendLineAt(
                1,
                accessibility
                    + " static "
                    + session
                    + " CreateEditSession(this "
                    + modelType
                    + " model, global::System.Action? onChanged = null) => "
                    + session
                    + ".Create(model, new "
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
                    + ", Invert = static changes => changes.Invert(), Rebase = static (changes, server) => changes.RebaseOnto(server), EnumerateChangedPaths = static changes => changes.EnumerateChangedPaths(), RefreshObservable = static observable => observable.__SparseRefresh() }, onChanged);"
            );
            code.AppendLineAt(
                1,
                "/// <summary>Creates a framework-neutral edit session that edits the supplied current model against a separate baseline snapshot.</summary>"
            );
            code.AppendLineAt(
                1,
                accessibility
                    + " static "
                    + session
                    + " CreateEditSession(this "
                    + modelType
                    + " baseline, "
                    + modelType
                    + " current, global::System.Action? onChanged = null) => "
                    + session
                    + ".Create(baseline, current, new "
                    + configuration
                    + " { FromModel = "
                    + modelType
                    + ".Fragment.From, Between = "
                    + modelType
                    + ".ChangeSet.Between, ToPatch = static changes => changes.ToPatch(), IsEmpty = static changes => changes.IsEmpty, AdvanceBaseline = static (changes, currentBaseline) => changes.ApplyToBaseline(currentBaseline), ToObservable = (value, changed, rawModelAccess) => new "
                    + modelType
                    + "."
                    + observable
                    + "(value, changed, rawModelAccess), ToCurrent = static value => new "
                    + modelType
                    + "."
                    + readOnlyView
                    + "(value), TryApplyTo = "
                    + tryApply
                    + ", WriteModel = "
                    + writeModel
                    + ", Invert = static changes => changes.Invert(), Rebase = static (changes, server) => changes.RebaseOnto(server), EnumerateChangedPaths = static changes => changes.EnumerateChangedPaths(), RefreshObservable = static observable => observable.__SparseRefresh() }, onChanged);"
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
}
