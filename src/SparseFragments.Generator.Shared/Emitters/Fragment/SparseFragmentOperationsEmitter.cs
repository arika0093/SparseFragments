using System.Collections.Immutable;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits the per-model Fragment operations class for split emission.</summary>
/// <remarks>
/// Stage 4 (#193): model-specific <c>Fragment</c> conversion, projection,
/// merge, diff, and deep-clone logic plus POCO clone helpers live in an
/// <c>internal static</c> operations class in the implementation source. The
/// model-facing <c>Fragment</c> keeps one-line facades. The class stays
/// internal so the public API surface is unchanged; member visibility follows
/// the model. <c>Fragment</c>/<c>FragmentBuilder</c> resolve through the
/// implementation file aliases, and nested models recurse through their own
/// facades.
/// </remarks>
internal static class SparseFragmentOperationsEmitter
{
    /// <summary>Appends the operations class with moved Fragment algorithms.</summary>
    /// <param name="code">Implementation target builder.</param>
    /// <param name="model">Model identity.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="pocoCloneModels">POCO clone helpers to relocate.</param>
    /// <param name="operationsCore">Core emitter bound to operations qualifiers.</param>
    /// <param name="isRootModel">Whether the projection constructor applies.</param>
    internal static void AppendOperations(
        SharedIndentedBuilder code,
        SparseModelInfo model,
        ImmutableArray<SparseMemberModel> members,
        ImmutableArray<SparsePocoCloneModel> pocoCloneModels,
        SparseFragmentCoreEmitter operationsCore,
        bool isRootModel = true
    )
    {
        var modelType = model.ModelTypeName;
        var modelIsReferenceType = !model.IsStruct;
        var usesPocoCloning = !pocoCloneModels.IsEmpty;
        var operationsName = SparseGeneratedPlacement.GetFragmentOperationsSimpleName(
            model,
            code.CancellationToken
        );
        code.AppendLineAt(
            1,
            "/// <summary>Per-model Fragment conversion, merge, diff, and clone operations.</summary>"
        );
        code.AppendLineAt(1, "internal static class " + operationsName);
        code.AppendLineAt(1, "{");
        operationsCore.AppendFromModel(
            code,
            modelType,
            members,
            modelIsReferenceType,
            usesPocoCloning
        );
        SparseFragmentCoreEmitter.AppendToModel(
            code,
            modelType,
            members,
            isRootModel,
            model.Constructor,
            receiver: "fragment."
        );
        operationsCore.AppendMerge(code, members, receiver: "self.");
        operationsCore.AppendApplyChanges(code, members, receiver: "self.");
        operationsCore.AppendDiff(code, modelType, members, modelIsReferenceType);
        operationsCore.AppendDeepClone(
            code,
            modelType,
            members,
            usesPocoCloning,
            model.Constructor,
            modelIsReferenceType,
            receiver: "value."
        );
        foreach (var poco in pocoCloneModels)
            operationsCore.AppendPocoCloneHelper(
                code,
                poco.Model.ModelTypeName,
                poco.CloneHelperName,
                poco.Members,
                poco.Model.Constructor,
                helperAccessibility: "internal"
            );
        code.AppendLineAt(1, "}");
    }
}
