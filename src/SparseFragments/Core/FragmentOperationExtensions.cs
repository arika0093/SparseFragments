using System;
using System.ComponentModel;

namespace SparseFragments;

/// <summary>
/// Presence-preserving mutation helpers for <see cref="FragmentOperation{T}"/> members.
/// </summary>
/// <remarks>
/// These are <c>ref</c> extensions so they mutate the referenced storage in place,
/// including the <c>ref</c>-returning members of generated <c>Patch</c> types
/// (for example <c>patch.Label.Set("x")</c>). The converting <c>CopyFrom</c> overload
/// invokes the converter only for a <see cref="FragmentOperationKind.Set"/> source;
/// <see cref="FragmentOperationKind.Unchanged"/> and <see cref="FragmentOperationKind.Unset"/>
/// sources are preserved as-is without invoking the converter.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public static class FragmentOperationExtensions
{
    /// <summary>Sets the referenced operation to <see cref="FragmentOperationKind.Set"/> with the supplied value.</summary>
    /// <param name="target">The operation storage to mutate.</param>
    /// <param name="value">The value to set, including an explicit null or default.</param>
    public static void Set<T>(this ref FragmentOperation<T> target, T? value) =>
        target = FragmentOperation<T>.Set(value);

    /// <summary>Sets the referenced operation to <see cref="FragmentOperationKind.Unset"/>.</summary>
    /// <param name="target">The operation storage to mutate.</param>
    public static void Unset<T>(this ref FragmentOperation<T> target) =>
        target = FragmentOperation<T>.Unset;

    /// <summary>Resets the referenced operation to <see cref="FragmentOperationKind.Unchanged"/>.</summary>
    /// <param name="target">The operation storage to mutate.</param>
    public static void SetUnchanged<T>(this ref FragmentOperation<T> target) =>
        target = FragmentOperation<T>.Unchanged;

    /// <summary>Copies another operation, preserving its kind and value.</summary>
    /// <param name="target">The operation storage to mutate.</param>
    /// <param name="source">The operation to copy.</param>
    public static void CopyFrom<T>(
        this ref FragmentOperation<T> target,
        FragmentOperation<T> source
    ) => target = source;

    /// <summary>
    /// Copies another operation while converting its value, preserving the operation kind.
    /// </summary>
    /// <remarks>
    /// Only a <see cref="FragmentOperationKind.Set"/> <paramref name="source"/> invokes
    /// <paramref name="map"/>; the converted value is stored with <c>Set</c>, so a null/default
    /// conversion result stays an explicit set. <see cref="FragmentOperationKind.Unchanged"/> and
    /// <see cref="FragmentOperationKind.Unset"/> sources are copied unchanged without invoking
    /// <paramref name="map"/>.
    /// </remarks>
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
        else if (source.Kind == FragmentOperationKind.Unset)
        {
            target = FragmentOperation<TDestination>.Unset;
        }
        else
        {
            target = FragmentOperation<TDestination>.Unchanged;
        }
    }
}
