using System;
using System.ComponentModel;

namespace SparseFragments;

/// <summary>Presence-preserving mutation helpers for <see cref="FragmentOperation{T}"/> members.</summary>
/// <remarks>These are <c>ref</c> extensions; converters run only for <see cref="FragmentOperationKind.Set"/> sources.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public static class FragmentOperationExtensions
{
    /// <summary>Sets the referenced operation to <see cref="FragmentOperationKind.Set"/> with the supplied value.</summary>
    /// <param name="target">The operation storage to mutate.</param>
    /// <param name="value">The value to set, including an explicit null or default.</param>
    public static void Set<T>(this ref FragmentOperation<T> target, T? value) =>
        target = FragmentOperation<T>.Set(value);

    /// <summary>Sets the referenced operation to <see cref="FragmentOperationKind.Remove"/>.</summary>
    /// <param name="target">The operation storage to mutate.</param>
    public static void Remove<T>(this ref FragmentOperation<T> target) =>
        target = FragmentOperation<T>.Remove;

    /// <summary>Keeps the referenced member unchanged.</summary>
    /// <param name="target">The operation storage to mutate.</param>
    public static void Keep<T>(this ref FragmentOperation<T> target) =>
        target = FragmentOperation<T>.Keep;

    /// <summary>Copies another operation, preserving its kind and value.</summary>
    /// <param name="target">The operation storage to mutate.</param>
    /// <param name="source">The operation to copy.</param>
    public static void CopyFrom<T>(
        this ref FragmentOperation<T> target,
        FragmentOperation<T> source
    ) => target = source;

    /// <summary>Copies another operation while converting its value, preserving the operation kind.</summary>
    /// <remarks>Only <see cref="FragmentOperationKind.Set"/> sources invoke <paramref name="map"/>.</remarks>
    /// <param name="target">The operation storage to mutate.</param>
    /// <param name="source">The operation to copy.</param>
    /// <param name="map">Converts a set source value to the destination value.</param>
    public static void CopyFrom<TSource, TDestination>(
        this ref FragmentOperation<TDestination> target,
        FragmentOperation<TSource> source,
        Func<TSource?, TDestination?> map
    )
    {
        ArgumentNullException.ThrowIfNull(map);
        if (source.Kind == FragmentOperationKind.Set)
        {
            target = FragmentOperation<TDestination>.Set(map(source.Value));
        }
        else if (source.Kind == FragmentOperationKind.Remove)
        {
            target = FragmentOperation<TDestination>.Remove;
        }
        else
        {
            target = FragmentOperation<TDestination>.Keep;
        }
    }
}
