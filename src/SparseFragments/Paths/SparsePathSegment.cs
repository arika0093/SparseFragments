using System.ComponentModel;
using System.Globalization;

namespace SparseFragments;

/// <summary>One typed step of a <see cref="SparsePath"/>.</summary>
/// <remarks>
/// Segments distinguish declared member identity, typed key identity, and
/// positional indexes. Keys compare with <see cref="object.Equals(object?, object?)"/>
/// plus key type, never by mere string rendering, so distinct keys with
/// identical display text stay distinct.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public readonly struct SparsePathSegment : IEquatable<SparsePathSegment>
{
    private readonly string _name;
    private readonly object? _key;
    private readonly Type? _keyType;
    private readonly int _index;

    private SparsePathSegment(
        SparsePathSegmentKind kind,
        string name,
        object? key,
        Type? keyType,
        int index
    )
    {
        Kind = kind;
        _name = name;
        _key = key;
        _keyType = keyType;
        _index = index;
    }

    /// <summary>Gets the segment kind.</summary>
    public SparsePathSegmentKind Kind { get; }

    /// <summary>Gets the member name for <see cref="SparsePathSegmentKind.Member"/> segments.</summary>
    public string Name => Kind == SparsePathSegmentKind.Member ? _name : string.Empty;

    /// <summary>Gets the typed key for <see cref="SparsePathSegmentKind.Key"/> segments.</summary>
    public object? Key => Kind == SparsePathSegmentKind.Key ? _key : null;

    /// <summary>Gets the key type for <see cref="SparsePathSegmentKind.Key"/> segments.</summary>
    public Type? KeyType => Kind == SparsePathSegmentKind.Key ? _keyType : null;

    /// <summary>Gets the position for <see cref="SparsePathSegmentKind.Index"/> segments.</summary>
    public int Index => Kind == SparsePathSegmentKind.Index ? _index : -1;

    /// <summary>Creates a member segment for a declared model member.</summary>
    /// <param name="name">The declared CLR member name.</param>
    public static SparsePathSegment Member(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new SparsePathSegment(SparsePathSegmentKind.Member, name, null, null, -1);
    }

    /// <summary>Creates a key segment carrying the actual typed key.</summary>
    /// <param name="key">The entry key.</param>
    /// <typeparam name="TKey">The key type.</typeparam>
    public static SparsePathSegment KeyOf<TKey>(TKey key) =>
        new(SparsePathSegmentKind.Key, string.Empty, key, typeof(TKey), -1);

    /// <summary>Creates a positional index segment.</summary>
    /// <param name="index">The zero-based position.</param>
    public static SparsePathSegment At(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new SparsePathSegment(SparsePathSegmentKind.Index, string.Empty, null, null, index);
    }

    /// <summary>Determines whether two segments have the same identity.</summary>
    /// <param name="other">The segment to compare.</param>
    public bool Equals(SparsePathSegment other)
    {
        if (Kind != other.Kind)
        {
            return false;
        }

        return Kind switch
        {
            SparsePathSegmentKind.Member => string.Equals(
                _name,
                other._name,
                StringComparison.Ordinal
            ),
            SparsePathSegmentKind.Index => _index == other._index,
            _ => _keyType == other._keyType && Equals(_key, other._key),
        };
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SparsePathSegment other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (int)Kind * 397;
            hash =
                (hash * 397)
                ^ (
                    Kind switch
                    {
                        SparsePathSegmentKind.Member => StringComparer.Ordinal.GetHashCode(_name),
                        SparsePathSegmentKind.Index => _index,
                        _ => (_keyType?.GetHashCode() ?? 0) * 397 ^ (_key?.GetHashCode() ?? 0),
                    }
                );
            return hash;
        }
    }

    /// <summary>Determines whether two segments have the same identity.</summary>
    public static bool operator ==(SparsePathSegment left, SparsePathSegment right) =>
        left.Equals(right);

    /// <summary>Determines whether two segments differ in identity.</summary>
    public static bool operator !=(SparsePathSegment left, SparsePathSegment right) =>
        !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() =>
        Kind switch
        {
            SparsePathSegmentKind.Member => _name,
            SparsePathSegmentKind.Index => "["
                + _index.ToString(CultureInfo.InvariantCulture)
                + "]",
            _ => "[\"" + SparsePath.EscapeKey(SparsePath.KeyText(_key)) + "\"]",
        };
}
