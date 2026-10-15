using System;
using System.Collections.Immutable;
using System.Text;
using SparseTypes.Generator.Shared;

namespace SparseTypes.Generator;

/// <summary>Merges per-model split sources into one compilation unit.</summary>
internal static class SparseTypesSingleFileMerger
{
    /// <summary>Merges the surface and additional per-model sources.</summary>
    /// <param name="surface">Model-facing surface source.</param>
    /// <param name="additional">Implementation and relocated sources.</param>
    /// <returns>A single compilable source.</returns>
    public static string Merge(string surface, ImmutableArray<SparseGeneratedSource> additional)
    {
        if (additional.IsDefaultOrEmpty)
            return surface;
        // File-level aliases hoist before the first namespace so the merged
        // unit keeps one header plus the union of using aliases.
        SplitFileHeader(surface, out var headerLength, out var bodyStart);
        var hoisted = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (var line in EnumerateSplitLines(surface, 0, headerLength))
        {
            seen.Add(line);
        }

        // Bodies stay slices of their original sources; only the small
        // header lines needed for deduplication are materialized.
        var bodies = new System.Collections.Generic.List<(string Source, int Start, int Length)>(
            additional.Length
        );
        var retainedLength = (surface.Length - bodyStart) + headerLength;
        for (var index = 0; index < additional.Length; index++)
        {
            var extraSource = additional[index].Source;
            SplitFileHeader(extraSource, out var extraHeaderLength, out var extraBodyStart);
            foreach (var line in EnumerateSplitLines(extraSource, 0, extraHeaderLength))
            {
                if (
                    seen.Add(line)
                    && (
                        line.StartsWith("using ", StringComparison.Ordinal)
                        || line.StartsWith("#pragma", StringComparison.Ordinal)
                    )
                )
                {
                    hoisted.Add(line);
                    retainedLength += line.Length + 2;
                }
            }

            StripLeadingHeader(extraSource, extraBodyStart, out var bodyOffset, out var bodyLength);
            bodies.Add((extraSource, bodyOffset, bodyLength));
            retainedLength += bodyLength + 2;
        }

        var builder = new StringBuilder(retainedLength);
        // An empty surface previously split into one blank header line, so
        // its header materialized as a single newline rather than empty.
        var headerEndsWithLineFeed =
            surface.Length == 0 || EndsWithLineFeed(surface, 0, headerLength);
        if (surface.Length == 0)
            builder.Append('\n');
        else
            AppendHeader(builder, surface, headerLength);
        if (hoisted.Count > 0)
        {
            if (!headerEndsWithLineFeed)
                builder.AppendLine();
            foreach (var line in hoisted)
                builder.AppendLine(line);
        }

        builder.Append(surface, bodyStart, surface.Length - bodyStart);
        if (!EndsWithLineFeed(surface, bodyStart, surface.Length - bodyStart))
            builder.AppendLine();
        foreach (var (source, start, length) in bodies)
        {
            builder.AppendLine();
            builder.Append(source, start, length);
            if (!EndsWithLineFeed(source, start, length))
                builder.AppendLine();
        }

        return builder.ToString();
    }

    // Locates the leading header (blank/comment/nullable/using/pragma lines)
    // by scanning line boundaries; the header is the raw slice before the
    // first body line, exactly as the previous split-and-join produced.
    private static void SplitFileHeader(string source, out int headerLength, out int bodyStart)
    {
        var offset = 0;
        while (offset < source.Length)
        {
            var lineEnd = source.IndexOf('\n', offset);
            var contentLength = (lineEnd < 0 ? source.Length : lineEnd) - offset;
            if (!IsHeaderLine(source, offset, contentLength))
                break;
            offset = lineEnd < 0 ? source.Length : lineEnd + 1;
        }

        bodyStart = offset;
        headerLength = offset;
    }

    private static bool IsHeaderLine(string source, int start, int length)
    {
        var end = start + length;
        var contentStart = start;
        var contentEnd = end;
        while (contentStart < contentEnd && char.IsWhiteSpace(source[contentStart]))
            contentStart++;
        while (contentEnd > contentStart && char.IsWhiteSpace(source[contentEnd - 1]))
            contentEnd--;
        var trimmedLength = contentEnd - contentStart;
        if (trimmedLength == 0)
            return true;
        return StartsWithAt(source, contentStart, trimmedLength, "//")
            || StartsWithAt(source, contentStart, trimmedLength, "#nullable")
            || StartsWithAt(source, contentStart, trimmedLength, "using ")
            || StartsWithAt(source, contentStart, trimmedLength, "#pragma");
    }

    private static bool StartsWithAt(string source, int start, int length, string value)
    {
        if (length < value.Length)
            return false;
        for (var i = 0; i < value.Length; i++)
        {
            if (source[start + i] != value[i])
                return false;
        }

        return true;
    }

    // Skips a stale leading auto-generated comment plus nullable directive
    // left at the top of an extracted body, scanning only that small prefix.
    private static void StripLeadingHeader(
        string source,
        int bodyStart,
        out int start,
        out int length
    )
    {
        start = bodyStart;
        var skippedAuto = false;
        var skippedNullable = false;
        while (start < source.Length)
        {
            var lineEnd = source.IndexOf('\n', start);
            var contentLength = (lineEnd < 0 ? source.Length : lineEnd) - start;
            var kind = ClassifyStrippableLine(source, start, contentLength);
            if (!skippedAuto && kind == StrippableLine.Comment)
            {
                start = lineEnd < 0 ? source.Length : lineEnd + 1;
                skippedAuto = true;
                continue;
            }

            if (skippedAuto && !skippedNullable && kind == StrippableLine.Nullable)
            {
                start = lineEnd < 0 ? source.Length : lineEnd + 1;
                skippedNullable = true;
                continue;
            }

            break;
        }

        length = source.Length - start;
    }

    private enum StrippableLine
    {
        Other,
        Comment,
        Nullable,
    }

    private static StrippableLine ClassifyStrippableLine(string source, int start, int length)
    {
        var end = start + length;
        while (start < end && char.IsWhiteSpace(source[start]))
            start++;
        while (end > start && char.IsWhiteSpace(source[end - 1]))
            end--;
        var trimmedLength = end - start;
        if (trimmedLength >= 2 && source[start] == '/' && source[start + 1] == '/')
            return StrippableLine.Comment;
        if (
            trimmedLength >= "#nullable".Length
            && StartsWithAt(source, start, trimmedLength, "#nullable")
        )
            return StrippableLine.Nullable;
        return StrippableLine.Other;
    }

    // Enumerates non-empty lines exactly like text.Split('\r', '\n',
    // RemoveEmptyEntries) but materializes only the requested slice range.
    private static System.Collections.Generic.IEnumerable<string> EnumerateSplitLines(
        string text,
        int start,
        int length
    )
    {
        var end = start + length;
        var offset = start;
        while (offset < end)
        {
            var boundary = offset;
            while (boundary < end && text[boundary] != '\r' && text[boundary] != '\n')
                boundary++;
            if (boundary > offset)
                yield return text.Substring(offset, boundary - offset);
            offset = boundary + 1;
        }
    }

    private static void AppendHeader(StringBuilder builder, string surface, int headerLength)
    {
        builder.Append(surface, 0, headerLength);
        if (headerLength > 0 && surface[headerLength - 1] != '\n')
            builder.Append('\n');
    }

    private static bool EndsWithLineFeed(string text, int start, int length) =>
        length > 0 && text[start + length - 1] == '\n';
}
