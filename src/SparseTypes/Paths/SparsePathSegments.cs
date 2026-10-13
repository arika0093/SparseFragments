using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;

namespace SparseTypes;

/// <summary>A read-only view of the segments in a path.</summary>
/// <remarks>The default value is empty. Direct indexing and enumeration allocate no objects.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public readonly struct SparsePathSegments : IReadOnlyList<SparsePathSegment>
{
    private readonly SparsePathSegment[]? _segments;

    internal SparsePathSegments(SparsePathSegment[] segments) => _segments = segments;

    /// <summary>Gets the number of segments.</summary>
    public int Count => _segments?.Length ?? 0;

    /// <summary>Gets the segment at the specified index.</summary>
    public SparsePathSegment this[int index] =>
        (_segments ?? Array.Empty<SparsePathSegment>())[index];

    /// <summary>Returns an enumerator over the segments.</summary>
    public Enumerator GetEnumerator() => new(_segments);

    IEnumerator<SparsePathSegment> IEnumerable<SparsePathSegment>.GetEnumerator() =>
        GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Enumerates the segments of a path without allocating an enumerator object.</summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public struct Enumerator : IEnumerator<SparsePathSegment>
    {
        private readonly SparsePathSegment[]? _segments;
        private int _index;

        internal Enumerator(SparsePathSegment[]? segments)
        {
            _segments = segments;
            _index = -1;
        }

        /// <inheritdoc />
        public readonly SparsePathSegment Current =>
            _segments is not null && (uint)_index < (uint)_segments.Length
                ? _segments[_index]
                : throw new InvalidOperationException();

        readonly object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            if (_segments is not null && _index < _segments.Length - 1)
            {
                _index++;
                return true;
            }
            _index = _segments?.Length ?? 0;
            return false;
        }

        /// <inheritdoc />
        public void Reset() => _index = -1;

        /// <inheritdoc />
        public readonly void Dispose() { }
    }
}
