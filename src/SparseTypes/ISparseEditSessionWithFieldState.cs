using System.ComponentModel;

namespace SparseTypes;

/// <summary>Framework-neutral contract for path-first semantic field-state queries.</summary>
/// <remarks>Generated sessions implement this interface only when the owning generator binds a session-capability contract and emits the state surface; hosts probe for it instead of reflecting over session members.</remarks>
/// <typeparam name="TModel">The live editable model type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseEditSessionWithFieldState<TModel>
    where TModel : class
{
    /// <summary>Queries the semantic field state at a typed path against the current baseline.</summary>
    /// <param name="path">The typed path to inspect.</param>
    /// <typeparam name="TValue">The addressed value type.</typeparam>
    /// <returns>The live field state with presence-aware endpoints.</returns>
    SparseFieldState<TValue> FieldState<TValue>(SparsePath<TModel, TValue> path);

    /// <summary>Queries the semantic field state at a model-agnostic path against the current baseline.</summary>
    /// <param name="path">The path to inspect.</param>
    /// <returns>The live field state with boxed endpoints.</returns>
    SparseFieldState<object?> FieldState(SparsePath path);
}
