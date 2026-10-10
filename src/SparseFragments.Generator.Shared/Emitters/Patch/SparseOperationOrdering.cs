namespace SparseFragments.Generator.Shared;

/// <summary>Orders relocated operation members internal-first.</summary>
/// <remarks>
/// Patch and ChangeSet operation containers are filled by many small
/// emitters, each appending its own internal entry points alongside private
/// helpers. Appending in call order therefore interleaves the two groups.
/// This helper reorders only top-level member declarations within one
/// container so every <c>internal</c> member precedes every <c>private</c>
/// member; relative order inside each group and all member bodies stay
/// byte-identical, so semantics never change.
/// </remarks>
internal static class SparseOperationOrdering
{
    /// <summary>Reorders operation text so internal members precede private ones.</summary>
    /// <param name="operations">Raw operation container body.</param>
    /// <returns>Reordered body with identical members.</returns>
    public static string ReorderInternalFirst(string operations)
    {
        if (string.IsNullOrEmpty(operations))
        {
            return operations;
        }

        var endsWithNewline = operations.EndsWith("\n", System.StringComparison.Ordinal);
        var rawLines = operations.Split('\n');
        // Determine the top-level indent from the first member line.
        var topIndent = -1;
        foreach (var raw in rawLines)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var trimmed = raw.TrimStart();
            if (
                trimmed.StartsWith("///", System.StringComparison.Ordinal)
                || trimmed.StartsWith("internal ", System.StringComparison.Ordinal)
                || trimmed.StartsWith("private ", System.StringComparison.Ordinal)
                || trimmed.StartsWith("public ", System.StringComparison.Ordinal)
            )
            {
                topIndent = raw.Length - trimmed.Length;
                break;
            }
        }

        if (topIndent < 0)
        {
            return operations;
        }

        var blocks = new System.Collections.Generic.List<System.Collections.Generic.List<string>>();
        System.Collections.Generic.List<string>? current = null;
        foreach (var raw in rawLines)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var trimmed = raw.TrimStart();
            var indent = raw.Length - trimmed.Length;
            var isSignature =
                indent == topIndent
                && (
                    trimmed.StartsWith("internal ", System.StringComparison.Ordinal)
                    || trimmed.StartsWith("private ", System.StringComparison.Ordinal)
                    || trimmed.StartsWith("public ", System.StringComparison.Ordinal)
                );
            var isDoc =
                indent == topIndent && trimmed.StartsWith("///", System.StringComparison.Ordinal);
            // A doc comment starts the next member only when the current block
            // already holds its signature; leading docs belong to the block.
            var startsNewBlock =
                isSignature
                || (
                    isDoc
                    && current is not null
                    && current.Exists(static line =>
                    {
                        var t = line.TrimStart();
                        return t.StartsWith("internal ", System.StringComparison.Ordinal)
                            || t.StartsWith("private ", System.StringComparison.Ordinal)
                            || t.StartsWith("public ", System.StringComparison.Ordinal);
                    })
                );
            if (startsNewBlock)
            {
                current = new System.Collections.Generic.List<string>();
                blocks.Add(current);
                current.Add(raw);
            }
            else
            {
                current ??= new System.Collections.Generic.List<string>();
                if (current.Count == 0 && blocks.Count == 0)
                {
                    blocks.Add(current);
                }
                else if (current.Count == 0)
                {
                    // Continuation without an open block cannot happen for
                    // well-formed emission; attach to the previous block.
                    current = blocks[^1];
                }

                current.Add(raw);
            }
        }

        static int Rank(System.Collections.Generic.List<string> block)
        {
            foreach (var line in block)
            {
                var t = line.TrimStart();
                if (t.StartsWith("///", System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (t.StartsWith("public ", System.StringComparison.Ordinal))
                {
                    return 0;
                }

                if (t.StartsWith("internal ", System.StringComparison.Ordinal))
                {
                    return 1;
                }

                if (t.StartsWith("private ", System.StringComparison.Ordinal))
                {
                    return 2;
                }

                return 1;
            }

            return 1;
        }

        var ordered = new System.Collections.Generic.List<string>();
        foreach (var rank in new[] { 0, 1, 2 })
        {
            ordered.AddRange(
                blocks.Where(block => Rank(block) == rank).SelectMany(static block => block)
            );
        }

        var result = string.Join("\n", ordered);
        if (endsWithNewline)
        {
            result += "\n";
        }

        return result;
    }
}
