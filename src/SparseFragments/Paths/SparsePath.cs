using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace SparseFragments;

/// <summary>The canonical typed path abstraction for sparse member navigation.</summary>
/// <remarks>
/// A path names a root model plus an ordered segment list: members, typed keys,
/// and positional indexes. Identity covers root type, segment kind, declared
/// member name, and typed key/index, so different roots, distinct key types,
/// and escaping-sensitive keys never collide. The string rendering
/// (<see cref="ToString"/>) keeps the historical wire grammar (dotted members,
/// <c>Name["key"]</c> JSON-escaped keys, <c>[n]</c> positions, <c>$root</c> for
/// the whole contribution) for display and transport; parsing restores the
/// segment structure with string keys.
/// <para>
/// Exact-match lookup compares whole paths. Ancestor and descendant queries use
/// <see cref="IsAncestorOf"/>, <see cref="IsDescendantOf"/>, and
/// <see cref="StartsWith"/> explicitly.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparsePath : IEquatable<SparsePath>
{
    private readonly SparsePathSegment[] _segments;

    /// <summary>Creates a path from a root type and segments.</summary>
    /// <param name="rootType">The root model type.</param>
    /// <param name="segments">The ordered segments from the root.</param>
    public SparsePath(Type rootType, IEnumerable<SparsePathSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(rootType);
        ArgumentNullException.ThrowIfNull(segments);
        RootType = rootType;
        _segments = segments.ToArray();
    }

    /// <summary>Creates a path from a root type and segments.</summary>
    /// <param name="rootType">The root model type.</param>
    /// <param name="segments">The ordered segments from the root.</param>
    public SparsePath(Type rootType, params SparsePathSegment[] segments)
        : this(rootType, (IEnumerable<SparsePathSegment>)segments) { }

    /// <summary>Gets the root model type this path navigates from.</summary>
    public Type RootType { get; }

    /// <summary>Gets the ordered segments from the root.</summary>
    public IReadOnlyList<SparsePathSegment> Segments => Array.AsReadOnly(_segments);

    /// <summary>Gets whether this path addresses the whole root contribution.</summary>
    public bool IsRoot => _segments.Length == 0;

    /// <summary>Gets the segment count from the root.</summary>
    public int Depth => _segments.Length;

    /// <summary>Creates the root path for a model type.</summary>
    /// <param name="rootType">The root model type.</param>
    public static SparsePath Root(Type rootType) => new(rootType);

    /// <summary>Creates the root path for a model type.</summary>
    /// <typeparam name="TModel">The root model type.</typeparam>
    public static SparsePath Root<TModel>() => new(typeof(TModel));

    /// <summary>Gets the parent path, or null for the root.</summary>
    public SparsePath? Parent
    {
        get
        {
            if (_segments.Length == 0)
            {
                return null;
            }

            var parent = new SparsePathSegment[_segments.Length - 1];
            Array.Copy(_segments, parent, parent.Length);
            return new SparsePath(RootType, parent);
        }
    }

    /// <summary>Appends a member segment for a declared model member.</summary>
    /// <param name="name">The declared CLR member name.</param>
    public SparsePath Member(string name) => Append(SparsePathSegment.Member(name));

    /// <summary>Appends a key segment carrying the actual typed key.</summary>
    /// <param name="key">The entry key.</param>
    /// <typeparam name="TKey">The key type.</typeparam>
    public SparsePath Key<TKey>(TKey key) => Append(SparsePathSegment.KeyOf(key));

    /// <summary>Appends a temporary-identity segment for an unassigned keyed entry.</summary>
    /// <param name="temporaryKey">The stable temporary identity.</param>
    public SparsePath TemporaryKey(Guid temporaryKey) =>
        Append(SparsePathSegment.TemporaryKeyOf(temporaryKey));

    /// <summary>Appends a positional index segment.</summary>
    /// <param name="index">The zero-based position.</param>
    public SparsePath At(int index) => Append(SparsePathSegment.At(index));

    /// <summary>Appends one segment.</summary>
    /// <param name="segment">The segment to append.</param>
    public SparsePath Append(SparsePathSegment segment)
    {
        var combined = new SparsePathSegment[_segments.Length + 1];
        Array.Copy(_segments, combined, _segments.Length);
        combined[_segments.Length] = segment;
        return new SparsePath(RootType, combined);
    }

    /// <summary>Appends another path's segments, keeping this root type.</summary>
    /// <param name="suffix">The path whose segments are appended.</param>
    /// <remarks>Used to prefix nested entries with their parent's path.</remarks>
    public SparsePath Append(SparsePath suffix)
    {
        ArgumentNullException.ThrowIfNull(suffix);
        if (suffix._segments.Length == 0)
        {
            return this;
        }

        var combined = new SparsePathSegment[_segments.Length + suffix._segments.Length];
        Array.Copy(_segments, combined, _segments.Length);
        Array.Copy(suffix._segments, 0, combined, _segments.Length, suffix._segments.Length);
        return new SparsePath(RootType, combined);
    }

    /// <summary>Prepends a member segment, keeping the same root type.</summary>
    /// <param name="name">The declared CLR member name.</param>
    public SparsePath PrependMember(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var combined = new SparsePathSegment[_segments.Length + 1];
        combined[0] = SparsePathSegment.Member(name);
        Array.Copy(_segments, 0, combined, 1, _segments.Length);
        return new SparsePath(RootType, combined);
    }

    /// <summary>Prepends another path's segments, adopting the prefix root type.</summary>
    /// <param name="prefix">The prefix path.</param>
    /// <remarks>Used to bubble nested conflicts and changes to their parent root.</remarks>
    public SparsePath Prepend(SparsePath prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (prefix._segments.Length == 0)
        {
            return new SparsePath(prefix.RootType, _segments);
        }

        var combined = new SparsePathSegment[prefix._segments.Length + _segments.Length];
        Array.Copy(prefix._segments, combined, prefix._segments.Length);
        Array.Copy(_segments, 0, combined, prefix._segments.Length, _segments.Length);
        return new SparsePath(prefix.RootType, combined);
    }

    /// <summary>Determines whether this path is a strict ancestor of another path.</summary>
    /// <param name="other">The candidate descendant.</param>
    public bool IsAncestorOf(SparsePath? other)
    {
        if (
            other is null
            || RootType != other.RootType
            || _segments.Length >= other._segments.Length
        )
        {
            return false;
        }

        for (var index = 0; index < _segments.Length; index++)
        {
            if (!_segments[index].Equals(other._segments[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Determines whether this path is a strict descendant of another path.</summary>
    /// <param name="other">The candidate ancestor.</param>
    public bool IsDescendantOf(SparsePath? other) => other is not null && other.IsAncestorOf(this);

    /// <summary>Determines whether this path starts with another path's segments.</summary>
    /// <param name="other">The candidate prefix, matched inclusively.</param>
    public bool StartsWith(SparsePath? other)
    {
        if (other is null || RootType != other.RootType)
        {
            return false;
        }

        if (other._segments.Length > _segments.Length)
        {
            return false;
        }

        for (var index = 0; index < other._segments.Length; index++)
        {
            if (!_segments[index].Equals(other._segments[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Determines whether two paths have the same identity.</summary>
    /// <param name="other">The path to compare.</param>
    public bool Equals(SparsePath? other)
    {
        if (other is null || RootType != other.RootType)
        {
            return false;
        }

        if (_segments.Length != other._segments.Length)
        {
            return false;
        }

        for (var index = 0; index < _segments.Length; index++)
        {
            if (!_segments[index].Equals(other._segments[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SparsePath other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = RootType.GetHashCode();
            foreach (var segment in _segments)
            {
                hash = (hash * 397) ^ segment.GetHashCode();
            }

            return hash;
        }
    }

    /// <summary>Determines whether two paths have the same identity.</summary>
    public static bool operator ==(SparsePath? left, SparsePath? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Determines whether two paths differ in identity.</summary>
    public static bool operator !=(SparsePath? left, SparsePath? right) => !(left == right);

    /// <summary>Renders the wire-compatible path text.</summary>
    /// <remarks>
    /// Member segments join with <c>.</c>; key segments render as
    /// <c>Name["key"]</c> with JSON-escaped key text; index segments render as
    /// <c>[n]</c>; the whole-root path renders as <c>$root</c>. This spelling is
    /// unchanged from the historical string-path grammar, so serialized
    /// payloads keep their shape.
    /// </remarks>
    public override string ToString()
    {
        if (_segments.Length == 0)
        {
            return "$root";
        }

        var builder = new StringBuilder();
        foreach (var segment in _segments)
        {
            switch (segment.Kind)
            {
                case SparsePathSegmentKind.Member:
                    if (builder.Length > 0)
                    {
                        builder.Append('.');
                    }

                    builder.Append(segment.Name);
                    break;
                case SparsePathSegmentKind.Index:
                    builder.Append('[');
                    builder.Append(segment.Index.ToString(CultureInfo.InvariantCulture));
                    builder.Append(']');
                    break;
                case SparsePathSegmentKind.TemporaryKey:
                    builder.Append("[temp:\"");
                    builder.Append(
                        EscapeKey(
                            KeyText(
                                segment.TemporaryKey?.ToString("D", CultureInfo.InvariantCulture)
                            )
                        )
                    );
                    builder.Append("\"]");
                    break;
                default:
                    builder.Append("[\"");
                    builder.Append(EscapeKey(KeyText(segment.Key)));
                    builder.Append("\"]");
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Parses wire-compatible path text for a root model type.</summary>
    /// <param name="rootType">The root model type.</param>
    /// <param name="text">The path text.</param>
    /// <exception cref="FormatException">The text is not a valid path.</exception>
    public static SparsePath Parse(Type rootType, string text)
    {
        ArgumentNullException.ThrowIfNull(rootType);
        ArgumentNullException.ThrowIfNull(text);
        if (!TryParse(rootType, text, out var path))
        {
            throw new FormatException("The path text '" + text + "' is not a valid sparse path.");
        }

        return path!;
    }

    /// <summary>Parses wire-compatible path text for a root model type.</summary>
    /// <param name="text">The path text.</param>
    /// <typeparam name="TModel">The root model type.</typeparam>
    /// <exception cref="FormatException">The text is not a valid path.</exception>
    public static SparsePath Parse<TModel>(string text) => Parse(typeof(TModel), text);

    /// <summary>Attempts to parse wire-compatible path text.</summary>
    /// <param name="rootType">The root model type.</param>
    /// <param name="text">The path text.</param>
    /// <param name="path">The parsed path, or null when parsing fails.</param>
    public static bool TryParse(Type rootType, string? text, out SparsePath? path)
    {
        path = null;
        if (rootType is null || text is null)
        {
            return false;
        }

        if (text.Length == 0 || string.Equals(text, "$root", StringComparison.Ordinal))
        {
            path = Root(rootType);
            return true;
        }

        var segments = new List<SparsePathSegment>();
        var index = 0;
        while (index < text.Length)
        {
            if (text[index] == '[')
            {
                if (!TryParseBracket(text, ref index, out var segment))
                {
                    return false;
                }

                segments.Add(segment);
                if (index < text.Length && text[index] == '.')
                {
                    index++;
                    if (index == text.Length)
                    {
                        return false;
                    }
                }

                continue;
            }

            var start = index;
            while (index < text.Length && text[index] != '.' && text[index] != '[')
            {
                index++;
            }

            if (index == start)
            {
                return false;
            }

            segments.Add(SparsePathSegment.Member(text.Substring(start, index - start)));
            if (index < text.Length && text[index] == '.')
            {
                index++;
                if (index == text.Length)
                {
                    return false;
                }
            }
        }

        path = new SparsePath(rootType, segments);
        return true;
    }

    /// <summary>Attempts to parse wire-compatible path text.</summary>
    /// <param name="text">The path text.</param>
    /// <param name="path">The parsed path, or null when parsing fails.</param>
    /// <typeparam name="TModel">The root model type.</typeparam>
    public static bool TryParse<TModel>(string? text, out SparsePath? path) =>
        TryParse(typeof(TModel), text, out path);

    private static bool TryParseBracket(string text, ref int index, out SparsePathSegment segment)
    {
        segment = default;
        index++;
        if (index < text.Length && text[index] == '"')
        {
            var builder = new StringBuilder();
            index++;
            var closed = false;
            while (index < text.Length)
            {
                var current = text[index];
                if (current == '\\' && index + 1 < text.Length)
                {
                    var next = text[index + 1];
                    switch (next)
                    {
                        case '"':
                        case '\\':
                        case '/':
                            builder.Append(next);
                            index += 2;
                            continue;
                        case 'b':
                            builder.Append('\b');
                            index += 2;
                            continue;
                        case 'f':
                            builder.Append('\f');
                            index += 2;
                            continue;
                        case 'n':
                            builder.Append('\n');
                            index += 2;
                            continue;
                        case 'r':
                            builder.Append('\r');
                            index += 2;
                            continue;
                        case 't':
                            builder.Append('\t');
                            index += 2;
                            continue;
                        case 'u':
                            if (
                                index + 5 < text.Length
                                && TryParseHex(
                                    text[index + 2],
                                    text[index + 3],
                                    text[index + 4],
                                    text[index + 5],
                                    out var code
                                )
                            )
                            {
                                builder.Append((char)code);
                                index += 6;
                                continue;
                            }

                            return false;
                        default:
                            return false;
                    }
                }

                if (current == '"')
                {
                    index++;
                    closed = true;
                    break;
                }

                builder.Append(current);
                index++;
            }

            if (!closed || index >= text.Length || text[index] != ']')
            {
                return false;
            }

            index++;
            segment = SparsePathSegment.KeyOf(builder.ToString());
            return true;
        }

        var start = index;
        while (index < text.Length && text[index] != ']')
        {
            index++;
        }

        if (index == start || index >= text.Length)
        {
            return false;
        }

        var raw = text.Substring(start, index - start);
        index++;
        if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var position))
        {
            segment = SparsePathSegment.At(position);
            return true;
        }

        // Temporary-identity segments round-trip explicitly so wire text never
        // degrades into a plain string key.
        if (
            raw.Length > 7
            && raw.StartsWith("temp:\"", StringComparison.Ordinal)
            && raw.EndsWith("\"", StringComparison.Ordinal)
            && Guid.TryParse(raw.Substring(6, raw.Length - 7), out var temporaryKey)
        )
        {
            segment = SparsePathSegment.TemporaryKeyOf(temporaryKey);
            return true;
        }

        segment = SparsePathSegment.KeyOf(raw);
        return true;
    }

    private static bool TryParseHex(char a, char b, char c, char d, out int value)
    {
        value = 0;
        foreach (var digit in new[] { a, b, c, d })
        {
            value <<= 4;
            if (digit >= '0' && digit <= '9')
            {
                value |= digit - '0';
            }
            else if (digit >= 'a' && digit <= 'f')
            {
                value |= digit - 'a' + 10;
            }
            else if (digit >= 'A' && digit <= 'F')
            {
                value |= digit - 'A' + 10;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    internal static string KeyText(object? key)
    {
        if (key is null)
        {
            return string.Empty;
        }

        if (key is string text)
        {
            return text;
        }

        if (key is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return key.GetType().ToString()
            + ":"
            + (Convert.ToString(key, CultureInfo.InvariantCulture) ?? string.Empty);
    }

    internal static string EscapeKey(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.Any(static c => c < 0x20 || c == '"' || c == '\\'))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (c < 0x20)
                    {
                        builder.Append("\\u");
                        builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.ToString();
    }
}
