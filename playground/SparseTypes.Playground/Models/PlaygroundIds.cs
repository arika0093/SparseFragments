using System.Globalization;

namespace SparseTypes.Playground.Models;

/// <summary>Deterministic prefixed numeric ID allocation for the playground editors.</summary>
/// <remarks>Case 3 and Case 4 previously retried random IDs in a finite namespace, which slows down as the namespace fills and never returns when it is full. Checking candidates against the reserved set terminates by construction.</remarks>
public static class PlaygroundIds
{
    /// <summary>Allocates the first free <c>prefix + n</c> ID outside the reserved set.</summary>
    /// <param name="prefix">The ID prefix, such as <c>q</c> or <c>t</c>.</param>
    /// <param name="reserved">IDs that must not be reused, including baseline IDs of removed rows.</param>
    /// <returns>A prefixed ID absent from <paramref name="reserved"/>.</returns>
    /// <remarks>Scanning <c>1</c> through <c>reserved.Count + 1</c> always finds a free candidate because there are more distinct candidates than reserved strings. There is no artificial numeric limit.</remarks>
    public static string Allocate(string prefix, IEnumerable<string> reserved)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        ArgumentNullException.ThrowIfNull(reserved);

        var taken = new HashSet<string>(reserved, StringComparer.Ordinal);
        for (var candidate = 1; candidate <= taken.Count + 1; candidate++)
        {
            var id = prefix + candidate.ToString(CultureInfo.InvariantCulture);
            if (!taken.Contains(id))
            {
                return id;
            }
        }

        // Unreachable: the loop above always returns.
        throw new InvalidOperationException("No free ID is available.");
    }
}
