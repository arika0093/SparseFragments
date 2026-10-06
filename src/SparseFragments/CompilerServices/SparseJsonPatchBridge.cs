using System.ComponentModel;
using System.Text.Json.Nodes;

namespace SparseFragments.CompilerServices;

/// <summary>
/// Minimal JSON Patch bridge for generated <c>FromJsonPatch</c>/<c>ToJsonPatch</c>.
/// The full document/pointer/engine implementation stays internal; generated
/// code passes only baseline JSON, patch bytes, and comparison options.
/// </summary>
/// <remarks>Generated-code plumbing: referenced by emitted code, not hand-written callers.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SparseJsonPatchBridge
{
    /// <summary>Applies UTF-8 JSON Patch bytes to a baseline JSON state.</summary>
    public static JsonNode? Apply(
        JsonNode? baseline,
        bool baselineIsAbsent,
        ReadOnlyMemory<byte> jsonPatch,
        StringComparison propertyNameComparison,
        out bool isAbsent
    )
    {
        var document = JsonPatchDocument.Parse(jsonPatch);
        var result = JsonPatchEngine.Apply(
            baseline,
            baselineIsAbsent,
            document,
            propertyNameComparison
        );
        isAbsent = result.IsAbsent;
        return result.Node;
    }

    /// <summary>Diffs two JSON states into serialized UTF-8 JSON Patch bytes.</summary>
    public static byte[] Diff(
        JsonNode? before,
        bool beforeIsAbsent,
        JsonNode? after,
        bool afterIsAbsent
    )
    {
        var document = JsonPatchEngine.Diff(before, beforeIsAbsent, after, afterIsAbsent);
        return JsonPatchEngine.Serialize(document);
    }
}
