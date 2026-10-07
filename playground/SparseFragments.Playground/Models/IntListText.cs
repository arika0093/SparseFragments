namespace SparseFragments.Playground.Models;

/// <summary>
/// UI-only comma/whitespace-separated text conversion for <c>List&lt;int&gt;</c> Scores.
/// The editable state stays <see cref="PlaygroundQuest.Scores"/>; this helper only
/// formats/parses the text-field presentation. Parsing is forgiving: unparsable
/// tokens are ignored, matching the previous row-wrapper behavior.
/// </summary>
public static class IntListText
{
    /// <summary>Formats scores for the text field.</summary>
    public static string Format(IReadOnlyList<int> values) => string.Join(", ", values);

    /// <summary>Parses comma- or whitespace-separated integers, ignoring invalid tokens.</summary>
    public static List<int> Parse(string text)
    {
        var result = new List<int>();
        foreach (
            var part in text.Split([',', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
        )
        {
            if (int.TryParse(part.Trim(), out var value))
            {
                result.Add(value);
            }
        }

        return result;
    }
}
