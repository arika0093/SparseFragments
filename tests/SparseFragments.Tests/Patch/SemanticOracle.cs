using System.Collections;

namespace SparseFragments.Tests;

/// <summary>
/// Independent semantic-state comparison for patch algebra tests.
/// </summary>
/// <remarks>
/// This oracle deliberately avoids <c>Patch.Between(...).IsEmpty</c>, fragment
/// <c>Equals</c>, and the runtime <c>AreEqual</c> helpers: every root and member value
/// is inspected directly, so a shared defect in <c>Between</c>/equality cannot make
/// algebra-law assertions pass incorrectly.
///
/// Explicit comparer policy:
/// - scalars compare with ordinal/value equality (<c>object.Equals</c>);
/// - sequences (including duplicates) compare order-sensitively, element-wise;
/// - sets compare order-independently in both directions using the supplied element
///   comparer (default <c>OrdinalIgnoreCase</c>, matching the declared comparer of the
///   <c>SetSettings</c> model);
/// - dictionaries compare order-independently using the supplied key comparer
///   (default <c>OrdinalIgnoreCase</c>, matching the declared comparer of the
///   <c>DictionarySettings</c> model) with scalar value equality;
/// - keyed collections compare order-sensitively (the final key sequence is
///   significant) with per-element field equality;
/// - plain-class list elements have no fragment identity and compare by structural
///   value (<c>Name</c>/<c>Count</c>).
///
/// Every reported difference names the concrete state/member path (for example
/// <c>root</c>, <c>RetryCount</c>, <c>Nested.Host</c> or <c>Plugins[1]</c>) instead of
/// returning a bare boolean.
/// </remarks>
internal static class SemanticOracle
{
    public static void AssertEqual(
        Optional<Settings.Fragment?> expected,
        Optional<Settings.Fragment?> actual,
        string? context = null
    ) => RequireEmpty(Differences(expected, actual), context);

    public static void AssertEqual(
        Optional<SetSettings.Fragment?> expected,
        Optional<SetSettings.Fragment?> actual,
        string? context = null,
        IEqualityComparer<string>? elementComparer = null
    ) => RequireEmpty(Differences(expected, actual, elementComparer), context);

    public static void AssertEqual(
        Optional<DictionarySettings.Fragment?> expected,
        Optional<DictionarySettings.Fragment?> actual,
        string? context = null,
        IEqualityComparer<string>? keyComparer = null
    ) => RequireEmpty(Differences(expected, actual, keyComparer), context);

    public static void AssertEqual(
        Optional<ScalarSequenceHolder.Fragment?> expected,
        Optional<ScalarSequenceHolder.Fragment?> actual,
        string? context = null
    ) => RequireEmpty(Differences(expected, actual), context);

    public static void AssertEqual(
        Optional<ClassListHolder.Fragment?> expected,
        Optional<ClassListHolder.Fragment?> actual,
        string? context = null
    ) => RequireEmpty(Differences(expected, actual), context);

    public static void AssertEqual(
        Optional<KeyedServerHolder.Fragment?> expected,
        Optional<KeyedServerHolder.Fragment?> actual,
        string? context = null
    ) => RequireEmpty(Differences(expected, actual), context);

    public static IReadOnlyList<string> Differences(
        Optional<Settings.Fragment?> expected,
        Optional<Settings.Fragment?> actual
    )
    {
        var differences = new List<string>();
        if (!CheckRootPresence(expected.IsPresent, actual.IsPresent, differences))
        {
            return differences;
        }

        var expectedFragment = expected.Value;
        var actualFragment = actual.Value;
        if (expectedFragment is null || actualFragment is null)
        {
            if (!ReferenceEquals(expectedFragment, actualFragment))
            {
                differences.Add(
                    $"root: expected {DescribePresent(expectedFragment)} but was {DescribePresent(actualFragment)}"
                );
            }

            return differences;
        }

        AddSettingsDifferences(expectedFragment, actualFragment, differences);
        return differences;
    }

    public static IReadOnlyList<string> Differences(
        Optional<SetSettings.Fragment?> expected,
        Optional<SetSettings.Fragment?> actual,
        IEqualityComparer<string>? elementComparer = null
    )
    {
        elementComparer ??= StringComparer.OrdinalIgnoreCase;
        var differences = new List<string>();
        if (!CheckRootPresence(expected.IsPresent, actual.IsPresent, differences))
        {
            return differences;
        }

        var expectedFragment = expected.Value;
        var actualFragment = actual.Value;
        if (expectedFragment is null || actualFragment is null)
        {
            if (!ReferenceEquals(expectedFragment, actualFragment))
            {
                differences.Add(
                    $"root: expected {DescribePresent(expectedFragment)} but was {DescribePresent(actualFragment)}"
                );
            }

            return differences;
        }

        AddSetDifferences(
            "Values",
            expectedFragment.Values,
            actualFragment.Values,
            differences,
            elementComparer
        );
        return differences;
    }

    public static IReadOnlyList<string> Differences(
        Optional<DictionarySettings.Fragment?> expected,
        Optional<DictionarySettings.Fragment?> actual,
        IEqualityComparer<string>? keyComparer = null
    )
    {
        keyComparer ??= StringComparer.OrdinalIgnoreCase;
        var differences = new List<string>();
        if (!CheckRootPresence(expected.IsPresent, actual.IsPresent, differences))
        {
            return differences;
        }

        var expectedFragment = expected.Value;
        var actualFragment = actual.Value;
        if (expectedFragment is null || actualFragment is null)
        {
            if (!ReferenceEquals(expectedFragment, actualFragment))
            {
                differences.Add(
                    $"root: expected {DescribePresent(expectedFragment)} but was {DescribePresent(actualFragment)}"
                );
            }

            return differences;
        }

        AddDictionaryDifferences(
            "Values",
            expectedFragment.Values,
            actualFragment.Values,
            differences,
            keyComparer
        );
        return differences;
    }

    public static IReadOnlyList<string> Differences(
        Optional<ScalarSequenceHolder.Fragment?> expected,
        Optional<ScalarSequenceHolder.Fragment?> actual
    )
    {
        var differences = new List<string>();
        if (!CheckRootPresence(expected.IsPresent, actual.IsPresent, differences))
        {
            return differences;
        }

        var expectedFragment = expected.Value;
        var actualFragment = actual.Value;
        if (expectedFragment is null || actualFragment is null)
        {
            if (!ReferenceEquals(expectedFragment, actualFragment))
            {
                differences.Add(
                    $"root: expected {DescribePresent(expectedFragment)} but was {DescribePresent(actualFragment)}"
                );
            }

            return differences;
        }

        if (
            CheckPresence(
                "Tags",
                expectedFragment.Tags.IsPresent,
                actualFragment.Tags.IsPresent,
                differences
            )
        )
        {
            AddOrderedSequenceDifferences(
                "Tags",
                expectedFragment.Tags.Value as IEnumerable,
                actualFragment.Tags.Value as IEnumerable,
                differences
            );
        }

        if (
            CheckPresence(
                "Numbers",
                expectedFragment.Numbers.IsPresent,
                actualFragment.Numbers.IsPresent,
                differences
            )
        )
        {
            AddOrderedSequenceDifferences(
                "Numbers",
                expectedFragment.Numbers.Value as IEnumerable,
                actualFragment.Numbers.Value as IEnumerable,
                differences
            );
        }

        return differences;
    }

    public static IReadOnlyList<string> Differences(
        Optional<ClassListHolder.Fragment?> expected,
        Optional<ClassListHolder.Fragment?> actual
    )
    {
        var differences = new List<string>();
        if (!CheckRootPresence(expected.IsPresent, actual.IsPresent, differences))
        {
            return differences;
        }

        var expectedFragment = expected.Value;
        var actualFragment = actual.Value;
        if (expectedFragment is null || actualFragment is null)
        {
            if (!ReferenceEquals(expectedFragment, actualFragment))
            {
                differences.Add(
                    $"root: expected {DescribePresent(expectedFragment)} but was {DescribePresent(actualFragment)}"
                );
            }

            return differences;
        }

        if (
            CheckPresence(
                "Items",
                expectedFragment.Items.IsPresent,
                actualFragment.Items.IsPresent,
                differences
            )
        )
        {
            AddClassListDifferences(
                "Items",
                expectedFragment.Items.Value,
                actualFragment.Items.Value,
                differences
            );
        }

        return differences;
    }

    public static IReadOnlyList<string> Differences(
        Optional<KeyedServerHolder.Fragment?> expected,
        Optional<KeyedServerHolder.Fragment?> actual
    )
    {
        var differences = new List<string>();
        if (!CheckRootPresence(expected.IsPresent, actual.IsPresent, differences))
        {
            return differences;
        }

        var expectedFragment = expected.Value;
        var actualFragment = actual.Value;
        if (expectedFragment is null || actualFragment is null)
        {
            if (!ReferenceEquals(expectedFragment, actualFragment))
            {
                differences.Add(
                    $"root: expected {DescribePresent(expectedFragment)} but was {DescribePresent(actualFragment)}"
                );
            }

            return differences;
        }

        if (
            CheckPresence(
                "Items",
                expectedFragment.Items.IsPresent,
                actualFragment.Items.IsPresent,
                differences
            )
        )
        {
            AddKeyedServerDifferences(
                "Items",
                expectedFragment.Items.Value,
                actualFragment.Items.Value,
                differences
            );
        }

        return differences;
    }

    private static void RequireEmpty(IReadOnlyList<string> differences, string? context)
    {
        if (differences.Count == 0)
        {
            return;
        }

        var heading =
            context is null ? "Semantic state mismatch:" : $"Semantic state mismatch ({context}):";
        differences.ShouldBeEmpty(
            heading
                + Environment.NewLine
                + string.Join(Environment.NewLine, differences.Select(difference => "  - " + difference))
        );
    }

    private static bool CheckRootPresence(
        bool expectedPresent,
        bool actualPresent,
        List<string> differences
    )
    {
        if (expectedPresent != actualPresent)
        {
            differences.Add(
                $"root: expected {(expectedPresent ? "present" : "Missing")} but was {(actualPresent ? "present" : "Missing")}"
            );
            return false;
        }

        return expectedPresent;
    }

    private static bool CheckPresence(
        string path,
        bool expectedPresent,
        bool actualPresent,
        List<string> differences
    )
    {
        if (expectedPresent != actualPresent)
        {
            differences.Add(
                $"{path}: expected {(expectedPresent ? "present" : "Missing")} but was {(actualPresent ? "present" : "Missing")}"
            );
            return false;
        }

        return expectedPresent;
    }

    private static string DescribePresent(object? value) =>
        value is null ? "present null" : "present fragment";

    private static void AddSettingsDifferences(
        Settings.Fragment expected,
        Settings.Fragment actual,
        List<string> differences
    )
    {
        if (
            CheckPresence(
                "Enabled",
                expected.Enabled.IsPresent,
                actual.Enabled.IsPresent,
                differences
            )
            && !Equals(expected.Enabled.Value, actual.Enabled.Value)
        )
        {
            differences.Add(
                $"Enabled: expected {Render(expected.Enabled.Value)} but was {Render(actual.Enabled.Value)}"
            );
        }

        if (
            CheckPresence(
                "RetryCount",
                expected.RetryCount.IsPresent,
                actual.RetryCount.IsPresent,
                differences
            )
            && !Equals(expected.RetryCount.Value, actual.RetryCount.Value)
        )
        {
            differences.Add(
                $"RetryCount: expected {Render(expected.RetryCount.Value)} but was {Render(actual.RetryCount.Value)}"
            );
        }

        if (
            CheckPresence("Label", expected.Label.IsPresent, actual.Label.IsPresent, differences)
            && !Equals(expected.Label.Value, actual.Label.Value)
        )
        {
            differences.Add(
                $"Label: expected {Render(expected.Label.Value)} but was {Render(actual.Label.Value)}"
            );
        }

        if (
            CheckPresence("Nested", expected.Nested.IsPresent, actual.Nested.IsPresent, differences)
        )
        {
            AddNestedDifferences("Nested", expected.Nested.Value, actual.Nested.Value, differences);
        }

        if (
            CheckPresence(
                "Plugins",
                expected.Plugins.IsPresent,
                actual.Plugins.IsPresent,
                differences
            )
        )
        {
            AddOrderedSequenceDifferences(
                "Plugins",
                expected.Plugins.Value as IEnumerable,
                actual.Plugins.Value as IEnumerable,
                differences
            );
        }
    }

    private static void AddNestedDifferences(
        string path,
        Nested.Fragment? expected,
        Nested.Fragment? actual,
        List<string> differences
    )
    {
        if (expected is null || actual is null)
        {
            if (!ReferenceEquals(expected, actual))
            {
                differences.Add(
                    $"{path}: expected {DescribePresent(expected)} but was {DescribePresent(actual)}"
                );
            }

            return;
        }

        if (
            CheckPresence(
                path + ".Host",
                expected.Host.IsPresent,
                actual.Host.IsPresent,
                differences
            )
            && !Equals(expected.Host.Value, actual.Host.Value)
        )
        {
            differences.Add(
                $"{path}.Host: expected {Render(expected.Host.Value)} but was {Render(actual.Host.Value)}"
            );
        }

        if (
            CheckPresence(path + ".Port", expected.Port.IsPresent, actual.Port.IsPresent, differences)
            && !Equals(expected.Port.Value, actual.Port.Value)
        )
        {
            differences.Add(
                $"{path}.Port: expected {Render(expected.Port.Value)} but was {Render(actual.Port.Value)}"
            );
        }
    }

    private static void AddOrderedSequenceDifferences(
        string path,
        IEnumerable? expected,
        IEnumerable? actual,
        List<string> differences
    )
    {
        if (expected is null || actual is null)
        {
            if (!ReferenceEquals(expected, actual))
            {
                differences.Add(
                    $"{path}: expected {Render(expected)} but was {Render(actual)}"
                );
            }

            return;
        }

        var expectedItems = expected.Cast<object?>().ToList();
        var actualItems = actual.Cast<object?>().ToList();
        if (expectedItems.Count != actualItems.Count)
        {
            differences.Add(
                $"{path}: expected {expectedItems.Count} element(s) but was {actualItems.Count}"
            );
        }

        for (var index = 0; index < Math.Min(expectedItems.Count, actualItems.Count); index++)
        {
            if (!Equals(expectedItems[index], actualItems[index]))
            {
                differences.Add(
                    $"{path}[{index}]: expected {Render(expectedItems[index])} but was {Render(actualItems[index])}"
                );
            }
        }
    }

    private static void AddSetDifferences(
        string path,
        Optional<ISet<string>> expected,
        Optional<ISet<string>> actual,
        List<string> differences,
        IEqualityComparer<string> elementComparer
    )
    {
        if (!CheckPresence(path, expected.IsPresent, actual.IsPresent, differences))
        {
            return;
        }

        var expectedValues = expected.Value;
        var actualValues = actual.Value;
        if (expectedValues is null || actualValues is null)
        {
            if (!ReferenceEquals(expectedValues, actualValues))
            {
                differences.Add(
                    $"{path}: expected {DescribePresent(expectedValues)} but was {DescribePresent(actualValues)}"
                );
            }

            return;
        }

        var comparerName = elementComparer.GetType().Name;
        foreach (var item in expectedValues)
        {
            if (!actualValues.Contains(item, elementComparer))
            {
                differences.Add(
                    $"{path}: expected element {Render(item)} is missing from actual (element comparer: {comparerName})"
                );
            }
        }

        foreach (var item in actualValues)
        {
            if (!expectedValues.Contains(item, elementComparer))
            {
                differences.Add(
                    $"{path}: actual contains unexpected element {Render(item)} (element comparer: {comparerName})"
                );
            }
        }
    }

    private static void AddDictionaryDifferences(
        string path,
        Optional<Dictionary<string, int>> expected,
        Optional<Dictionary<string, int>> actual,
        List<string> differences,
        IEqualityComparer<string> keyComparer
    )
    {
        if (!CheckPresence(path, expected.IsPresent, actual.IsPresent, differences))
        {
            return;
        }

        var expectedValues = expected.Value;
        var actualValues = actual.Value;
        if (expectedValues is null || actualValues is null)
        {
            if (!ReferenceEquals(expectedValues, actualValues))
            {
                differences.Add(
                    $"{path}: expected {DescribePresent(expectedValues)} but was {DescribePresent(actualValues)}"
                );
            }

            return;
        }

        var comparerName = keyComparer.GetType().Name;
        foreach (var pair in expectedValues)
        {
            if (!TryGetValueWithComparer(actualValues, pair.Key, keyComparer, out var actualValue))
            {
                differences.Add(
                    $"{path}: expected key {Render(pair.Key)} is missing from actual (key comparer: {comparerName})"
                );
            }
            else if (!Equals(pair.Value, actualValue))
            {
                differences.Add(
                    $"{path}[{Render(pair.Key)}]: expected {Render(pair.Value)} but was {Render(actualValue)}"
                );
            }
        }

        foreach (var pair in actualValues)
        {
            if (!ContainsKeyWithComparer(expectedValues, pair.Key, keyComparer))
            {
                differences.Add(
                    $"{path}: actual contains unexpected key {Render(pair.Key)} (key comparer: {comparerName})"
                );
            }
        }
    }

    private static bool TryGetValueWithComparer(
        Dictionary<string, int> source,
        string key,
        IEqualityComparer<string> keyComparer,
        out int value
    )
    {
        foreach (var pair in source)
        {
            if (keyComparer.Equals(pair.Key, key))
            {
                value = pair.Value;
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static bool ContainsKeyWithComparer(
        Dictionary<string, int> source,
        string key,
        IEqualityComparer<string> keyComparer
    ) => TryGetValueWithComparer(source, key, keyComparer, out _);

    private static void AddClassListDifferences(
        string path,
        IReadOnlyList<ListChildItem>? expected,
        IReadOnlyList<ListChildItem>? actual,
        List<string> differences
    )
    {
        if (expected is null || actual is null)
        {
            if (!ReferenceEquals(expected, actual))
            {
                differences.Add(
                    $"{path}: expected {Render(expected)} but was {Render(actual)}"
                );
            }

            return;
        }

        if (expected.Count != actual.Count)
        {
            differences.Add($"{path}: expected {expected.Count} element(s) but was {actual.Count}");
        }

        for (var index = 0; index < Math.Min(expected.Count, actual.Count); index++)
        {
            // Plain-class elements carry no fragment identity, so compare by structural value.
            if (!Equals(expected[index].Name, actual[index].Name))
            {
                differences.Add(
                    $"{path}[{index}].Name: expected {Render(expected[index].Name)} but was {Render(actual[index].Name)}"
                );
            }

            if (!Equals(expected[index].Count, actual[index].Count))
            {
                differences.Add(
                    $"{path}[{index}].Count: expected {Render(expected[index].Count)} but was {Render(actual[index].Count)}"
                );
            }
        }
    }

    private static void AddKeyedServerDifferences(
        string path,
        IReadOnlyList<KeyedServer>? expected,
        IReadOnlyList<KeyedServer>? actual,
        List<string> differences
    )
    {
        if (expected is null || actual is null)
        {
            if (!ReferenceEquals(expected, actual))
            {
                differences.Add(
                    $"{path}: expected {Render(expected)} but was {Render(actual)}"
                );
            }

            return;
        }

        if (expected.Count != actual.Count)
        {
            differences.Add(
                $"{path}: expected {expected.Count} element(s) in final key sequence but was {actual.Count}"
            );
        }

        for (var index = 0; index < Math.Min(expected.Count, actual.Count); index++)
        {
            if (!Equals(expected[index].Id, actual[index].Id))
            {
                differences.Add(
                    $"{path}[{index}].Id (key): expected {Render(expected[index].Id)} but was {Render(actual[index].Id)}"
                );
            }

            if (!Equals(expected[index].Name, actual[index].Name))
            {
                differences.Add(
                    $"{path}[{index}].Name: expected {Render(expected[index].Name)} but was {Render(actual[index].Name)}"
                );
            }

            if (!Equals(expected[index].Count, actual[index].Count))
            {
                differences.Add(
                    $"{path}[{index}].Count: expected {Render(expected[index].Count)} but was {Render(actual[index].Count)}"
                );
            }
        }
    }

    private static string Render(object? value) =>
        value switch
        {
            null => "null",
            string text => $"\"{text}\"",
            IEnumerable items => $"[{string.Join(", ", items.Cast<object?>().Select(Render))}]",
            _ => value.ToString() ?? "null",
        };
}
