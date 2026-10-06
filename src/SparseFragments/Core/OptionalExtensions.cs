using System;
using System.ComponentModel;

namespace SparseFragments;

/// <summary>
/// Presence-preserving mutation helpers for <see cref="Optional{T}"/> members.
/// </summary>
/// <remarks>
/// These are <c>ref</c> extensions so they mutate the referenced storage in place,
/// including the <c>ref</c>-returning members of generated <c>FragmentBuilder</c> types
/// (for example <c>builder.Label.Set("x")</c>). Every helper preserves presence exactly:
/// <c>Missing</c> stays missing and a present value — including an explicit null or
/// <c>default</c> — stays present. The converting <c>CopyFrom</c> overload invokes the
/// converter only for a present source value.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public static class OptionalExtensions
{
    /// <summary>Makes the referenced member present with the supplied value, including null or default.</summary>
    /// <param name="target">The member storage to mutate.</param>
    /// <param name="value">The value to store as present.</param>
    public static void Set<T>(this ref Optional<T> target, T? value) =>
        target = Optional<T>.Present(value);

    /// <summary>Makes the referenced member missing.</summary>
    /// <param name="target">The member storage to mutate.</param>
    public static void Unset<T>(this ref Optional<T> target) => target = Optional<T>.Missing;

    /// <summary>Copies the presence and value of another member.</summary>
    /// <param name="target">The member storage to mutate.</param>
    /// <param name="source">The member state to copy; missing stays missing.</param>
    public static void CopyFrom<T>(this ref Optional<T> target, Optional<T> source) =>
        target = source;

    /// <summary>
    /// Copies another member while converting its value, preserving presence exactly.
    /// </summary>
    /// <remarks>
    /// A missing <paramref name="source"/> leaves <paramref name="target"/> missing and never
    /// invokes <paramref name="map"/>. A present source — including a present null or default —
    /// stores <c>Present(map(source.Value))</c>, so a null/default conversion result stays present.
    /// </remarks>
    /// <param name="target">The member storage to mutate.</param>
    /// <param name="source">The member state to copy; missing stays missing.</param>
    /// <param name="map">Converts a present source value to the destination value.</param>
    public static void CopyFrom<TSource, TDestination>(
        this ref Optional<TDestination> target,
        Optional<TSource> source,
        Func<TSource?, TDestination?> map
    )
    {
        ArgumentNullException.ThrowIfNull(map);
        target = source.IsPresent
            ? Optional<TDestination>.Present(map(source.Value))
            : Optional<TDestination>.Missing;
    }
}
