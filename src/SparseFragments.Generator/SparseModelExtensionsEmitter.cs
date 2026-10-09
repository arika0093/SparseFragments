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
        SparseGeneratorConfig config
    )
    {
        var cancellationToken = code.CancellationToken;
        var modelType = model.ModelTypeName;
        var extensionClass = ContainerName(model, cancellationToken);
        var family = config.EffectiveFamilyNames;
        var observableSimple = SparseObservableEmitter.ObservableTypeName(members, family);
        var accessibility = model.IsPublic ? "public" : "internal";
        var readOnlySimple = SparseReadOnlyViewEmitter.ReadOnlyViewTypeName(members, family);
        var implementationNamespace = SparseGeneratedPlacement.TryGetImplementationNamespace(
            config
        );
        var relocated = implementationNamespace is not null;
        var observableRef = relocated
            ? SparseGeneratedPlacement.GetObservableReference(
                model,
                observableSimple,
                config,
                cancellationToken
            )
            : modelType + "." + observableSimple;

        code.AppendLine();
        if (!model.IsStruct && !relocated)
        {
            SparseEditSessionEmitter.AppendModelEditSession(
                code,
                model,
                modelType,
                accessibility,
                observableSimple,
                readOnlySimple,
                members,
                config
            );
        }

        code.AppendLineAt(
            0,
            "/// <summary>Model extensions generated for this fragment model.</summary>"
        );
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
            var editSession = relocated
                ? SparseGeneratedPlacement.GetEditSessionReference(model, config, cancellationToken)
                : modelType + ".EditSession";
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
                    + observableRef
                    + "?> ToObservable(this global::SparseFragments.Optional<"
                    + modelType
                    + "?> model, global::System.Action? onChanged = null)"
            );
            code.AppendLineAt(1, "{");
            code.AppendLineAt(
                2,
                "if (!model.IsPresent) return global::SparseFragments.Optional<"
                    + observableRef
                    + "?>.Missing;"
            );
            code.AppendLineAt(2, "var value = model.Value;");
            code.AppendLineAt(
                2,
                "return global::SparseFragments.Optional<"
                    + observableRef
                    + "?>.Present(value is null ? null : new "
                    + observableRef
                    + "(value, onChanged));"
            );
            code.AppendLineAt(1, "}");
        }

        code.AppendLineAt(0, "}");
    }
}
