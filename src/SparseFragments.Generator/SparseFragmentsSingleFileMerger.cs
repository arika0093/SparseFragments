using System;
using System.Collections.Immutable;
using System.Text;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Generator;

/// <summary>Merges per-model split sources into one compilation unit.</summary>
internal static class SparseFragmentsSingleFileMerger
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
        var (header, body) = SplitFileHeader(surface);
        var hoisted = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (var line in SplitLines(header))
        {
            seen.Add(line);
        }

        var bodies = new System.Collections.Generic.List<string>();
        foreach (var source in additional)
        {
            var (extraHeader, extraBody) = SplitFileHeader(source.Source);
            foreach (var line in SplitLines(extraHeader))
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
                }
            }

            bodies.Add(StripLeadingHeader(extraBody));
        }

        var builder = new StringBuilder(body.Length + (1024 * bodies.Count));
        builder.Append(header);
        if (hoisted.Count > 0)
        {
            if (!header.EndsWith("\n", StringComparison.Ordinal))
                builder.AppendLine();
            foreach (var line in hoisted)
                builder.AppendLine(line);
        }

        builder.Append(body);
        if (!body.EndsWith("\n", StringComparison.Ordinal))
            builder.AppendLine();
        foreach (var extra in bodies)
        {
            builder.AppendLine();
            builder.Append(extra);
            if (!extra.EndsWith("\n", StringComparison.Ordinal))
                builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string[] SplitLines(string text) =>
        text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

    private static (string Header, string Body) SplitFileHeader(string source)
    {
        var lines = source.Split('\n');
        var headerEnd = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (
                trimmed.Length == 0
                || trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.StartsWith("#nullable", StringComparison.Ordinal)
                || trimmed.StartsWith("using ", StringComparison.Ordinal)
                || trimmed.StartsWith("#pragma", StringComparison.Ordinal)
            )
            {
                headerEnd = i + 1;
                continue;
            }

            break;
        }

        if (headerEnd <= 0)
            return (string.Empty, source);
        var header = string.Join("\n", lines, 0, headerEnd);
        if (!header.EndsWith("\n", StringComparison.Ordinal))
            header += "\n";
        var body = string.Join("\n", lines, headerEnd, lines.Length - headerEnd);
        return (header, body);
    }

    private static string StripLeadingHeader(string body)
    {
        var lines = body.Split('\n');
        var start = 0;
        var skippedAuto = false;
        var skippedNullable = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (!skippedAuto && trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                start = i + 1;
                skippedAuto = true;
                continue;
            }

            if (
                skippedAuto
                && !skippedNullable
                && trimmed.StartsWith("#nullable", StringComparison.Ordinal)
            )
            {
                start = i + 1;
                skippedNullable = true;
                continue;
            }

            break;
        }

        if (start <= 0)
            return body;
        return string.Join("\n", lines, start, lines.Length - start);
    }
}
