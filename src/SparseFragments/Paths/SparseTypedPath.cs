using System.ComponentModel;

namespace SparseFragments;

/// <summary>A compile-time typed <see cref="SparsePath"/> for one root model and value type.</summary>
/// <remarks>
/// Generated fluent builders (for example <c>Order.SparsePath.Items.Key(id).Price</c>)
/// produce these paths, so wrong key, value, or root types fail at compile time on
/// typed APIs. The path converts implicitly to <see cref="SparsePath"/> for
/// model-agnostic storage, comparison, and lookup.
/// </remarks>
/// <typeparam name="TModel">The root model type.</typeparam>
/// <typeparam name="TValue">The addressed value type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparsePath<TModel, TValue> : IEquatable<SparsePath<TModel, TValue>>
{
    /// <summary>Creates a typed path over an untyped path.</summary>
    /// <param name="path">The underlying path.</param>
    /// <exception cref="ArgumentException">The path root is not <typeparamref name="TModel"/>.</exception>
    public SparsePath(SparsePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.RootType != typeof(TModel))
        {
            throw new ArgumentException(
                "The path root '" + path.RootType + "' is not '" + typeof(TModel) + "'.",
                nameof(path)
            );
        }

        Path = path;
    }

    /// <summary>Gets the underlying model-agnostic path.</summary>
    public SparsePath Path { get; }

    /// <summary>Gets the root model type.</summary>
    public Type RootType => typeof(TModel);

    /// <summary>Gets the addressed value type.</summary>
    public Type ValueType => typeof(TValue);

    /// <summary>Converts a typed path to its model-agnostic form.</summary>
    /// <param name="path">The typed path.</param>
    public static implicit operator SparsePath(SparsePath<TModel, TValue>? path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Path;
    }

    /// <summary>Determines whether two typed paths have the same identity.</summary>
    /// <param name="other">The path to compare.</param>
    public bool Equals(SparsePath<TModel, TValue>? other) =>
        other is not null && Path.Equals(other.Path);

    /// <summary>Determines whether this path equals a model-agnostic path.</summary>
    /// <param name="other">The path to compare.</param>
    public bool Equals(SparsePath? other) => other is not null && Path.Equals(other);

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is SparsePath<TModel, TValue> typed
            ? Equals(typed)
            : obj is SparsePath untyped && Equals(untyped);

    /// <inheritdoc />
    public override int GetHashCode() => Path.GetHashCode();

    /// <summary>Determines whether two typed paths have the same identity.</summary>
    public static bool operator ==(
        SparsePath<TModel, TValue>? left,
        SparsePath<TModel, TValue>? right
    ) => left is null ? right is null : left.Equals(right);

    /// <summary>Determines whether two typed paths differ in identity.</summary>
    public static bool operator !=(
        SparsePath<TModel, TValue>? left,
        SparsePath<TModel, TValue>? right
    ) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => Path.ToString();
}
