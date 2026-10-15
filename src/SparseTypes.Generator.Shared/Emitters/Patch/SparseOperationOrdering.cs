using System.Collections.Generic;
using System.Text;

namespace SparseTypes.Generator.Shared;

/// <summary>Orders relocated operation members internal-first.</summary>
/// <remarks>Patch and ChangeSet operation containers are filled by many small emitters.</remarks>
internal static class SparseOperationOrdering
{
    private enum LineKind
    {
        Other,
        Documentation,
        Public,
        Internal,
        Private,
    }

    /// <summary>Reorders operation text so internal members precede private ones.</summary>
    /// <param name="operations">Raw operation container body.</param>
    /// <returns>Reordered body with identical members.</returns>
    public static string ReorderInternalFirst(string operations)
    {
        var blocks = FindBlocks(operations);
        if (blocks is null)
            return operations;
        var result = new StringBuilder(operations.Length);
        for (var rank = 0; rank < 3; rank++)
        {
            foreach (var block in blocks)
            {
                if (block.Rank == rank)
                    AppendBlock(result, operations, block.Start, block.End);
            }
        }
        if (operations[operations.Length - 1] == '\n')
            result.Append('\n');
        return result.ToString();
    }

    /// <summary>Appends reordered operation members directly to the destination source builder.</summary>
    /// <param name="code">Destination source builder.</param>
    /// <param name="operations">Raw operation container body.</param>
    public static void AppendInternalFirst(SharedIndentedBuilder code, string operations)
    {
        var blocks = FindBlocks(operations);
        if (blocks is null)
        {
            code.Append(operations);
            return;
        }
        var hasLine = false;
        for (var rank = 0; rank < 3; rank++)
        {
            foreach (var block in blocks)
            {
                if (block.Rank == rank)
                    AppendBlock(code, operations, block.Start, block.End, ref hasLine);
            }
        }
        if (operations[operations.Length - 1] == '\n')
            code.Append('\n');
    }

    private static List<(int Start, int End, int Rank)>? FindBlocks(string operations)
    {
        if (string.IsNullOrEmpty(operations))
            return null;
        var topIndent = FindTopIndent(operations);
        if (topIndent < 0)
            return null;

        var blocks = new List<(int Start, int End, int Rank)>();
        var blockStart = -1;
        var blockRank = 1;
        var hasRank = false;
        var hasSignature = false;
        for (var start = 0; start < operations.Length; )
        {
            var line = ReadLine(operations, start);
            if (line.TrimmedStart < line.End)
            {
                var signature =
                    line.Kind is LineKind.Public or LineKind.Internal or LineKind.Private;
                var topLevel = line.TrimmedStart - start == topIndent;
                if (
                    blockStart < 0
                    || (
                        topLevel
                        && (signature || (line.Kind == LineKind.Documentation && hasSignature))
                    )
                )
                {
                    if (blockStart >= 0)
                        blocks.Add((blockStart, start, blockRank));
                    blockStart = start;
                    blockRank = 1;
                    hasRank = false;
                    hasSignature = false;
                }
                if (!hasRank && line.Kind != LineKind.Documentation)
                {
                    blockRank = line.Kind switch
                    {
                        LineKind.Public => 0,
                        LineKind.Private => 2,
                        _ => 1,
                    };
                    hasRank = true;
                }
                hasSignature |= signature;
            }
            start = line.End + 1;
        }
        if (blockStart >= 0)
            blocks.Add((blockStart, operations.Length, blockRank));

        return blocks;
    }

    private static int FindTopIndent(string source)
    {
        for (var start = 0; start < source.Length; )
        {
            var line = ReadLine(source, start);
            if (line.Kind != LineKind.Other)
                return line.TrimmedStart - start;
            start = line.End + 1;
        }
        return -1;
    }

    private static (int End, int TrimmedStart, LineKind Kind) ReadLine(string source, int start)
    {
        var end = source.IndexOf('\n', start);
        if (end < 0)
            end = source.Length;
        var trimmed = TrimStart(source, start, end);
        var kind = LineKind.Other;
        if (HasPrefix(source, trimmed, end, "///"))
            kind = LineKind.Documentation;
        else if (HasPrefix(source, trimmed, end, "public "))
            kind = LineKind.Public;
        else if (HasPrefix(source, trimmed, end, "internal "))
            kind = LineKind.Internal;
        else if (HasPrefix(source, trimmed, end, "private "))
            kind = LineKind.Private;
        return (end, trimmed, kind);
    }

    private static int TrimStart(string source, int start, int end)
    {
        while (start < end && char.IsWhiteSpace(source[start]))
            start++;
        return start;
    }

    private static bool HasPrefix(string source, int start, int end, string prefix) =>
        end - start >= prefix.Length
        && string.CompareOrdinal(source, start, prefix, 0, prefix.Length) == 0;

    private static void AppendBlock(StringBuilder result, string source, int start, int end)
    {
        while (start < end)
        {
            var lineEnd = source.IndexOf('\n', start);
            if (lineEnd < 0 || lineEnd > end)
                lineEnd = end;
            if (TrimStart(source, start, lineEnd) < lineEnd)
            {
                if (result.Length != 0)
                    result.Append('\n');
                result.Append(source, start, lineEnd - start);
            }
            start = lineEnd + 1;
        }
    }

    private static void AppendBlock(
        SharedIndentedBuilder code,
        string source,
        int start,
        int end,
        ref bool hasLine
    )
    {
        while (start < end)
        {
            var lineEnd = source.IndexOf('\n', start);
            if (lineEnd < 0 || lineEnd > end)
                lineEnd = end;
            if (TrimStart(source, start, lineEnd) < lineEnd)
            {
                if (hasLine)
                    code.Append('\n');
                code.Append(source, start, lineEnd - start);
                hasLine = true;
            }
            start = lineEnd + 1;
        }
    }
}
