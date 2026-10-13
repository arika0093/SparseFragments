namespace SparseTypes.Playground.Pages.Case3;

/// <summary>Converts the Case 3 score-list text field to and from integer lists.</summary>
public static class IntListText
{
    /// <summary>Formats scores for the text field.</summary>
    public static string Format(IReadOnlyList<int> values) => string.Join(", ", values);

    /// <summary>Parses comma- or whitespace-separated integers, ignoring invalid tokens.</summary>
    public static List<int> Parse(string text)
    {
        var result = new List<int>();
        foreach (
            var part in text.Split(
                [',', ' ', '\n', '\r', '\t'],
                StringSplitOptions.RemoveEmptyEntries
            )
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
