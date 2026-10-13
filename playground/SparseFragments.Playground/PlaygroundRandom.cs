namespace SparseFragments.Playground;

internal static class PlaygroundRandom
{
    private static readonly string[] NameWords =
    [
        "Alder",
        "Bluebird",
        "Cedar",
        "Golden",
        "Harbor",
        "Juniper",
        "Maple",
        "Northstar",
        "Riverstone",
        "Willow",
    ];

    private static readonly string[] BusinessTypes =
    [
        "Bakery",
        "Cafe",
        "Coffee",
        "Kitchen",
        "Market",
        "Roasters",
    ];

    internal static string CreateDifferentCustomerName(string current)
    {
        var first = Pick(NameWords);
        var second = Pick(NameWords);
        var candidate = Random.Shared.Next(4) switch
        {
            0 => $"Cafe {first}",
            1 => $"{first} {Pick(BusinessTypes)}",
            2 => $"{first} & {second}",
            _ => $"The {first} {second} {Pick(BusinessTypes)}",
        };

        var distinctCandidate = candidate;
        var suffix = 2;
        while (string.Equals(distinctCandidate, current, StringComparison.Ordinal))
        {
            distinctCandidate = $"{candidate} {suffix++}";
        }

        return distinctCandidate;
    }

    private static string Pick(string[] values) => values[Random.Shared.Next(values.Length)];
}
