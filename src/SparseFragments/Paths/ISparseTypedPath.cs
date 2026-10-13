using System.ComponentModel;

namespace SparseFragments;

/// <summary>A typed path that exposes its model-agnostic path representation.</summary>
/// <typeparam name="TModel">The root model type.</typeparam>
/// <typeparam name="TValue">The addressed value type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseTypedPath<TModel, TValue>
{
    /// <summary>Gets the compile-time typed path.</summary>
    SparsePath<TModel, TValue> TypedPath { get; }

    /// <summary>Gets the underlying model-agnostic path.</summary>
    SparsePath UntypedPath { get; }

    /// <summary>Gets whether the addressed value can be updated without replacing its instance.</summary>
    bool CanWriteModelValueInPlace { get; }

    /// <summary>Writes an updated model value into an existing addressed instance.</summary>
    /// <param name="current">The existing value to preserve.</param>
    /// <param name="updated">The updated value to copy into the existing instance.</param>
    void WriteModelValueInPlace(TValue current, TValue updated);
}
