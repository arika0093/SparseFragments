using System;
using System.Collections.Generic;
using System.Threading;

namespace SparseTypes.Generator.Shared;

/// <summary>Literal- and comment-aware text replacement for generated sources.</summary>
/// <remarks>Relocation rewrites match fully qualified code fragments.</remarks>
internal static class SparseCodeRewrite
{
    private static readonly char[] CodeBoundaries = { '/', '#', '\'', '"' };
    private static readonly char[] HoleBoundaries = { '/', '#', '\'', '"', '{', '}' };
    private static readonly char[] CharBoundaries = { '\\', '\'', '\n' };
    private static readonly char[] StringBoundaries = { '\\', '"', '\n' };
    private static readonly char[] VerbatimBoundaries = { '"' };
    private static readonly char[] InterpolatedBoundaries = { '\\', '{', '}', '"', '\n' };
    private static readonly char[] InterpolatedVerbatimBoundaries = { '{', '}', '"' };

    /// <summary>Replaces occurrences anchored outside literals and comments.</summary>
    /// <param name="source">Generated source to rewrite.</param>
    /// <param name="oldValue">Text to replace; empty values match nothing.</param>
    /// <param name="newValue">Replacement text.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>The rewritten source.</returns>
    public static string ReplaceOutsideLiteralsAndComments(
        string source,
        string oldValue,
        string newValue,
        CancellationToken cancellationToken
    )
    {
        if (oldValue.Length == 0)
        {
            return source;
        }

        var match = source.IndexOf(oldValue, StringComparison.Ordinal);
        if (match < 0)
        {
            return source;
        }

        using var code = new SparseRewriteBuffer(source, oldValue, newValue, match);
        RewriteCode(source, code, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return code.BuildResult();
    }

    private enum ScanKind
    {
        Code,
        HoleCode,
        LineComment,
        BlockComment,
        Char,
        String,
        VerbatimString,
        Interpolated,
        InterpolatedVerbatim,
    }

    // Replace at code positions during scanning to avoid retaining a character
    // mask.
    private static void RewriteCode(
        string source,
        SparseRewriteBuffer code,
        CancellationToken cancellationToken
    )
    {
        var frames = new Stack<ScanKind>();
        frames.Push(ScanKind.Code);
        var index = 0;
        while (index < source.Length && code.HasCandidate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index = frames.Peek() switch
            {
                ScanKind.LineComment => ScanLineComment(source, code, frames, index),
                ScanKind.BlockComment => ScanBlockComment(source, frames, index),
                ScanKind.Char => ScanCharLiteral(source, frames, index),
                ScanKind.String => ScanString(source, frames, index),
                ScanKind.VerbatimString => ScanVerbatimString(source, frames, index),
                ScanKind.Interpolated => ScanInterpolated(source, code, frames, index, false),
                ScanKind.InterpolatedVerbatim => ScanInterpolated(
                    source,
                    code,
                    frames,
                    index,
                    true
                ),
                ScanKind.HoleCode => ScanHoleCode(source, code, frames, index),
                _ => ScanCode(source, code, frames, index),
            };
        }
    }

    private static int ScanCode(
        string source,
        SparseRewriteBuffer code,
        Stack<ScanKind> frames,
        int index
    )
    {
        var c = source[index];
        if (c == '/' && index + 1 < source.Length && source[index + 1] == '/')
        {
            frames.Push(ScanKind.LineComment);
            return index + 2;
        }

        if (c == '/' && index + 1 < source.Length && source[index + 1] == '*')
        {
            frames.Push(ScanKind.BlockComment);
            return index + 2;
        }

        // Preprocessor directives run to end of line.
        if (c == '#')
        {
            frames.Push(ScanKind.LineComment);
            return index + 1;
        }

        if (c == '\'')
        {
            frames.Push(ScanKind.Char);
            return index + 1;
        }

        if (c == '"')
        {
            if (IsRawStringOpen(source, index))
            {
                return SkipRawString(source, index);
            }

            frames.Push(StringKind(source, index));
            return index + 1;
        }

        code.MarkCode(index);
        return NextCodePosition(source, code.NextCandidate, frames.Peek(), index);
    }

    private static int NextCodePosition(string source, int candidate, ScanKind kind, int index)
    {
        if (candidate < 0)
            return source.Length;
        // Only lexical boundaries or replacement candidates can change the scan
        // state.
        var boundary = source.IndexOfAny(
            kind == ScanKind.HoleCode ? HoleBoundaries : CodeBoundaries,
            index + 1,
            candidate - index - 1
        );
        return boundary < 0 ? candidate : boundary;
    }

    // Interpolation holes are code: braces balance against HoleCode frames so
    // the closing brace returns to the gap instead of ending the string.
    private static int ScanHoleCode(
        string source,
        SparseRewriteBuffer code,
        Stack<ScanKind> frames,
        int index
    )
    {
        var c = source[index];
        if (c == '{')
        {
            code.MarkCode(index);
            frames.Push(ScanKind.HoleCode);
            return index + 1;
        }

        if (c == '}')
        {
            code.MarkCode(index);
            frames.Pop();
            return index + 1;
        }

        return ScanCode(source, code, frames, index);
    }

    private static int ScanLineComment(
        string source,
        SparseRewriteBuffer code,
        Stack<ScanKind> frames,
        int index
    )
    {
        var newline = source.IndexOf('\n', index);
        if (newline < 0)
            return source.Length;
        code.MarkCode(newline);
        frames.Pop();
        return newline + 1;
    }

    private static int ScanBlockComment(string source, Stack<ScanKind> frames, int index)
    {
        var closer = source.IndexOf("*/", index, StringComparison.Ordinal);
        if (closer < 0)
            return source.Length;
        frames.Pop();
        return closer + 2;
    }

    private static int ScanCharLiteral(string source, Stack<ScanKind> frames, int index)
    {
        if (source[index] == '\\' && index + 1 < source.Length)
        {
            return index + 2;
        }

        if (source[index] == '\'' || source[index] == '\n')
        {
            frames.Pop();
            return index + 1;
        }

        return NextBoundary(source, CharBoundaries, index);
    }

    private static int ScanString(string source, Stack<ScanKind> frames, int index)
    {
        if (source[index] == '\\' && index + 1 < source.Length)
        {
            return index + 2;
        }

        // An unterminated quote is invalid code; recover to code scanning.
        if (source[index] == '"' || source[index] == '\n')
        {
            frames.Pop();
            return index + 1;
        }

        return NextBoundary(source, StringBoundaries, index);
    }

    private static int ScanVerbatimString(string source, Stack<ScanKind> frames, int index)
    {
        if (source[index] == '"')
        {
            if (index + 1 < source.Length && source[index + 1] == '"')
            {
                return index + 2;
            }

            frames.Pop();
            return index + 1;
        }

        return NextBoundary(source, VerbatimBoundaries, index);
    }

    private static int ScanInterpolated(
        string source,
        SparseRewriteBuffer code,
        Stack<ScanKind> frames,
        int index,
        bool verbatim
    )
    {
        var c = source[index];
        if (verbatim && c == '"' && index + 1 < source.Length && source[index + 1] == '"')
        {
            return index + 2;
        }
        if (!verbatim && c == '\\' && index + 1 < source.Length)
        {
            return index + 2;
        }

        if ((c == '{' || c == '}') && index + 1 < source.Length && source[index + 1] == c)
        {
            return index + 2;
        }

        if (c == '{')
        {
            code.MarkCode(index);
            frames.Push(ScanKind.HoleCode);
            return index + 1;
        }

        // A lone closing brace is invalid in a gap; recover to code scanning.
        if (c == '"' || c == '}' || (!verbatim && c == '\n'))
        {
            frames.Pop();
            return index + (c == '"' ? 1 : 0);
        }

        return NextBoundary(
            source,
            verbatim ? InterpolatedVerbatimBoundaries : InterpolatedBoundaries,
            index
        );
    }

    private static int NextBoundary(string source, char[] boundaries, int index)
    {
        var boundary = source.IndexOfAny(boundaries, index + 1);
        return boundary < 0 ? source.Length : boundary;
    }

    private static ScanKind StringKind(string source, int index)
    {
        var interpolated =
            (index > 0 && source[index - 1] == '$')
            || (index > 1 && source[index - 1] == '@' && source[index - 2] == '$');
        if (!interpolated)
        {
            return index > 0 && source[index - 1] == '@'
                ? ScanKind.VerbatimString
                : ScanKind.String;
        }

        var verbatim =
            (index > 1 && source[index - 1] == '$' && source[index - 2] == '@')
            || (index > 1 && source[index - 1] == '@' && source[index - 2] == '$');
        return verbatim ? ScanKind.InterpolatedVerbatim : ScanKind.Interpolated;
    }

    private static bool IsRawStringOpen(string source, int index) =>
        index + 2 < source.Length && source[index + 1] == '"' && source[index + 2] == '"';

    // Raw strings cannot nest, so the closing run is found directly without a
    // frame.
    private static int SkipRawString(string source, int index)
    {
        var quotes = 0;
        while (index + quotes < source.Length && source[index + quotes] == '"')
        {
            quotes++;
        }

        var next = index + quotes;
        while (next < source.Length)
        {
            var end = source.IndexOf('"', next);
            if (end < 0)
                return source.Length;
            var length = 1;
            while (length < quotes && end + length < source.Length && source[end + length] == '"')
                length++;
            if (length == quotes)
                return end + quotes;
            next = end + length;
        }
        return source.Length;
    }
}
