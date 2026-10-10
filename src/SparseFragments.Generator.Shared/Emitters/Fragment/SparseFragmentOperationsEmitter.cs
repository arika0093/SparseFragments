using System.Collections.Immutable;
using System.Linq;

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
        // Public-first order: public operations precede internal bridges,
        // with private Diff helpers last.
        var requiresFromContext = members.Any(static member =>
            member.ChildModel is not null
            || member.Property.Type.PocoCloneHelperName is not null
            || member.Collection.CloneKind != SparseCloneCollectionKind.Unsupported
        );
        operationsCore.AppendFromModelPublicBody(
            code,
            modelType,
            members,
            modelIsReferenceType,
            requiresFromContext
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
        // Origin queries are public surface; they precede Diff so the
        // public-first order (public ops, internal bridges, private Diff
        // helpers) holds when origins are enabled.
        operationsCore.AppendOriginOperations(code, members, modelType);
        operationsCore.AppendDiffPublicBody(code, modelType, members, modelIsReferenceType);
        operationsCore.AppendDeepCloneOperationsPublic(
            code,
            modelType,
            members,
            model.Constructor,
            modelIsReferenceType
        );
        operationsCore.AppendFromModelInternalBody(code, modelType, members, modelIsReferenceType);
        SparseFragmentCoreEmitter.AppendDiffInternalBody(code, modelType, modelIsReferenceType);
        operationsCore.AppendDeepCloneOperationsInternal(
            code,
            modelType,
            members,
            model.Constructor,
            modelIsReferenceType
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
        operationsCore.AppendDiffPrivateBodies(code, modelType, members, modelIsReferenceType);
        code.AppendLineAt(1, "}");
    }
}
