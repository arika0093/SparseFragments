using System.Collections.Immutable;
using System.Threading;
using SparseTypes.Generator.Shared;

namespace SparseTypes.Generator;

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
            // Session collision resolution mirrors the emitter: a same-named
            // member pushes the session behind a Sparse prefix.
            var sessionSimple = SparseGeneratedPlacement.ResolveUiTypeName(
                "EditSession",
                members.Select(static member => member.Property.Name)
            );
            var editSession = modelType + "." + sessionSimple;
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
                    + " static global::SparseTypes.Optional<"
                    + observableRef
                    + "?> ToObservable(this global::SparseTypes.Optional<"
                    + modelType
                    + "?> model, global::System.Action? onChanged = null)"
            );
            code.AppendLineAt(1, "{");
            code.AppendLineAt(
                2,
                "if (!model.IsPresent) return global::SparseTypes.Optional<"
                    + observableRef
                    + "?>.Missing;"
            );
            code.AppendLineAt(2, "var value = model.Value;");
            code.AppendLineAt(
                2,
                "return global::SparseTypes.Optional<"
                    + observableRef
                    + "?>.Present(value is null ? null : new "
                    + observableRef
                    + "(value, onChanged));"
            );
            code.AppendLineAt(1, "}");
        }

        if (
            !model.IsStruct
            && config.DescriptorDialect is not null
            && config.EffectiveEmissionFeatures.EmitObservable
        )
        {
            // Session-rooted collection editing facade (issue #251): the
            // overloads below only forward the live descriptor set and the
            // batch runner to the runtime editors, so all semantics stay in
            // the reusable runtime while call sites read session-first.
            var sessionType =
                modelType
                + "."
                + SparseGeneratedPlacement.ResolveUiTypeName(
                    "EditSession",
                    members.Select(static member => member.Property.Name)
                );
            code.AppendLineAt(
                1,
                "/// <summary>Opens a path-addressed typed editing facade for one collection member of this session.</summary>"
            );
            code.AppendLineAt(
                1,
                "/// <remarks>Each operation re-resolves the live collection and runs inside one batch, so a replaced collection never acts on stale state and one user action raises one transition notification. Keyed lookups resolve stable identity per operation and never treat a sentinel or position as durable identity. For state inspection rather than editing, read the typed ChangeSet projections instead.</remarks>"
            );
            code.AppendLineAt(
                1,
                "/// <typeparam name=\"TElement\">The collection element model type.</typeparam>"
            );
            code.AppendLineAt(1, "/// <param name=\"session\">The edit session.</param>");
            code.AppendLineAt(
                1,
                "/// <param name=\"collectionPath\">A single-member collection path.</param>"
            );
            code.AppendLineAt(
                1,
                "/// <returns>An editing facade bound to the addressed member.</returns>"
            );
            code.AppendLineAt(
                1,
                accessibility
                    + " static global::SparseTypes.SparseCollectionEditor<TElement> EditCollection<TElement>(this "
                    + sessionType
                    + " session, global::SparseTypes.SparsePath collectionPath)"
            );
            code.AppendLineAt(1, "{");
            code.AppendLineAt(
                2,
                "if (session is null) throw new global::System.ArgumentNullException(nameof(session));"
            );
            code.AppendLineAt(
                2,
                "return global::SparseTypes.SparseCollectionEditors.EditCollection<TElement>(session.Descriptors, collectionPath, session.BatchEdit);"
            );
            code.AppendLineAt(1, "}");
            code.AppendLineAt(
                1,
                "/// <summary>Opens a path-addressed keyed editing facade for one collection member of this session.</summary>"
            );
            code.AppendLineAt(
                1,
                "/// <remarks>Keyed operations resolve the current index per operation, so rows survive reordering; unassigned sentinels never serve as durable identity. Temporary identities address pending rows through their stable Guid.</remarks>"
            );
            code.AppendLineAt(
                1,
                "/// <typeparam name=\"TElement\">The collection element model type.</typeparam>"
            );
            code.AppendLineAt(
                1,
                "/// <typeparam name=\"TKey\">The assigned key type declared by the key attribute.</typeparam>"
            );
            code.AppendLineAt(1, "/// <param name=\"session\">The edit session.</param>");
            code.AppendLineAt(
                1,
                "/// <param name=\"collectionPath\">A single-member collection path.</param>"
            );
            code.AppendLineAt(
                1,
                "/// <returns>A keyed editing facade bound to the addressed member.</returns>"
            );
            code.AppendLineAt(
                1,
                accessibility
                    + " static global::SparseTypes.SparseKeyedCollectionEditor<TElement, TKey> EditCollection<TElement, TKey>(this "
                    + sessionType
                    + " session, global::SparseTypes.SparsePath collectionPath)"
            );
            code.AppendLineAt(1, "{");
            code.AppendLineAt(
                2,
                "if (session is null) throw new global::System.ArgumentNullException(nameof(session));"
            );
            code.AppendLineAt(
                2,
                "return global::SparseTypes.SparseCollectionEditors.EditKeyedCollection<TElement, TKey>(session.Descriptors, collectionPath, session.BatchEdit);"
            );
            code.AppendLineAt(1, "}");
        }

        code.AppendLineAt(0, "}");
    }
}
