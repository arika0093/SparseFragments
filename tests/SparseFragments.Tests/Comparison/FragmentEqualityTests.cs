namespace SparseFragments.Tests;

public sealed class FragmentEqualityTests
{
    [Test]
    public void EmptyFragmentsAreEqual()
    {
        AreEqual(new Settings.Fragment(), new Settings.Fragment()).ShouldBeTrue();
    }

    [Test]
    public void SameInstanceIsEqual()
    {
        var fragment = new Settings.Fragment { Enabled = Optional<bool>.Present(false) };
        AreEqual(fragment, fragment).ShouldBeTrue();
    }

    [Test]
    public void NullHandling()
    {
        AreEqual(null, null).ShouldBeTrue();
        AreEqual(new Settings.Fragment(), null).ShouldBeFalse();
        AreEqual(null, new Settings.Fragment()).ShouldBeFalse();
    }

    [Test]
    public void MissingPresentNullAndPresentValueRemainDistinct()
    {
        var missing = new Settings.Fragment();
        var presentNull = new Settings.Fragment { Label = Optional<string?>.Present(null) };
        var presentValue = new Settings.Fragment { Label = Optional<string?>.Present("x") };
        var otherNull = new Settings.Fragment { Label = Optional<string?>.Present(null) };

        AreEqual(missing, presentNull).ShouldBeFalse();
        AreEqual(presentNull, presentValue).ShouldBeFalse();
        AreEqual(presentNull, otherNull).ShouldBeTrue();
    }

    [Test]
    public void DifferentPresentCountsAreUnequal()
    {
        var one = new Settings.Fragment { Enabled = Optional<bool>.Present(false) };
        var two = new Settings.Fragment
        {
            Enabled = Optional<bool>.Present(false),
            RetryCount = Optional<int>.Present(3),
        };

        AreEqual(one, two).ShouldBeFalse();
        AreEqual(two, one).ShouldBeFalse();
    }

    [Test]
    public void SameCountDifferentMembersAreUnequal()
    {
        var left = new Settings.Fragment { Enabled = Optional<bool>.Present(false) };
        var right = new Settings.Fragment { RetryCount = Optional<int>.Present(3) };

        AreEqual(left, right).ShouldBeFalse();
    }

    [Test]
    public void DifferentFragmentTypesAreUnequal()
    {
        var settings = new Settings.Fragment { RetryCount = Optional<int>.Present(1) };
        var nested = new Nested.Fragment { Port = Optional<int>.Present(1) };

        Settings.Patch
            .Between(
                Optional<Settings.Fragment?>.Present(settings),
                Optional<Settings.Fragment?>.Missing
            )
            .IsEmpty.ShouldBeFalse();
        Nested.Patch
            .Between(
                Optional<Nested.Fragment?>.Present(nested),
                Optional<Nested.Fragment?>.Missing
            )
            .IsEmpty.ShouldBeFalse();
    }

    [Test]
    public void NestedFragmentsCompareStructurally()
    {
        var left = new Settings.Fragment
        {
            Nested = Optional<Nested.Fragment?>.Present(
                new Nested.Fragment { Host = Optional<string>.Present("db.local") }
            ),
        };
        var right = new Settings.Fragment
        {
            Nested = Optional<Nested.Fragment?>.Present(
                new Nested.Fragment { Host = Optional<string>.Present("db.local") }
            ),
        };
        var changed = new Settings.Fragment
        {
            Nested = Optional<Nested.Fragment?>.Present(
                new Nested.Fragment
                {
                    Host = Optional<string>.Present("db.local"),
                    Port = Optional<int>.Present(6432),
                }
            ),
        };
        var missing = new Settings.Fragment();

        AreEqual(left, right).ShouldBeTrue();
        AreEqual(left, changed).ShouldBeFalse();
        AreEqual(left, missing).ShouldBeFalse();
    }

    [Test]
    public void NestedFragmentVersusNullIsUnequal()
    {
        var fragment = new Settings.Fragment
        {
            Nested = Optional<Nested.Fragment?>.Present(
                new Nested.Fragment { Host = Optional<string>.Present("db.local") }
            ),
        };
        var presentNull = new Settings.Fragment
        {
            Nested = Optional<Nested.Fragment?>.Present(null),
        };

        AreEqual(fragment, presentNull).ShouldBeFalse();
        AreEqual(
            presentNull,
            new Settings.Fragment { Nested = Optional<Nested.Fragment?>.Present(null) }
        ).ShouldBeTrue();
    }

    [Test]
    public void SequencesAreOrderSensitive()
    {
        var left = new Settings.Fragment
        {
            Plugins = Optional<IReadOnlyList<string>>.Present(["a", "b"]),
        };
        var same = new Settings.Fragment
        {
            Plugins = Optional<IReadOnlyList<string>>.Present(["a", "b"]),
        };
        var reordered = new Settings.Fragment
        {
            Plugins = Optional<IReadOnlyList<string>>.Present(["b", "a"]),
        };
        var shorter = new Settings.Fragment
        {
            Plugins = Optional<IReadOnlyList<string>>.Present(["a"]),
        };

        AreEqual(left, same).ShouldBeTrue();
        AreEqual(left, reordered).ShouldBeFalse();
        AreEqual(left, shorter).ShouldBeFalse();
    }

    [Test]
    public void ArraysAndListsWithEqualContentAreEqual()
    {
        AreEqual(
            new Settings.Fragment
            {
                Plugins = Optional<IReadOnlyList<string>>.Present(["a", "b"]),
            },
            new Settings.Fragment
            {
                Plugins = Optional<IReadOnlyList<string>>.Present(["a", "b"]),
            }
        ).ShouldBeTrue();
    }

    [Test]
    public void DictionariesAreOrderIndependent()
    {
        var left = new DictionarySettings.Fragment
        {
            Values = Optional<Dictionary<string, int>>.Present(
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["first"] = 1,
                    ["second"] = 2,
                }
            ),
        };
        var reordered = new DictionarySettings.Fragment
        {
            Values = Optional<Dictionary<string, int>>.Present(
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["second"] = 2,
                    ["first"] = 1,
                }
            ),
        };
        var changed = new DictionarySettings.Fragment
        {
            Values = Optional<Dictionary<string, int>>.Present(
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["first"] = 1,
                    ["second"] = 3,
                }
            ),
        };

        DictionaryAreEqual(left, reordered).ShouldBeTrue();
        DictionaryAreEqual(left, changed).ShouldBeFalse();
    }

    [Test]
    public void SetsAreOrderIndependentAndUseNativeSemantics()
    {
        var left = new SetSettings.Fragment
        {
            Values = Optional<ISet<string>>.Present(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alpha", "beta" }
            ),
        };
        var reordered = new SetSettings.Fragment
        {
            Values = Optional<ISet<string>>.Present(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BETA", "ALPHA" }
            ),
        };
        var changed = new SetSettings.Fragment
        {
            Values = Optional<ISet<string>>.Present(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alpha", "gamma" }
            ),
        };

        SetAreEqual(left, reordered).ShouldBeTrue();
        SetAreEqual(left, changed).ShouldBeFalse();
    }

    [Test]
    public void ScalarsAndNulls()
    {
        AreEqual(
            new Settings.Fragment { RetryCount = Optional<int>.Present(1) },
            new Settings.Fragment { RetryCount = Optional<int>.Present(1) }
        ).ShouldBeTrue();
        AreEqual(
            new Settings.Fragment { RetryCount = Optional<int>.Present(1) },
            new Settings.Fragment { RetryCount = Optional<int>.Present(2) }
        ).ShouldBeFalse();
        AreEqual(
            new Settings.Fragment { Label = Optional<string?>.Present("a") },
            new Settings.Fragment { Label = Optional<string?>.Present("a") }
        ).ShouldBeTrue();
        AreEqual(
            new Settings.Fragment { Label = Optional<string?>.Present("a") },
            new Settings.Fragment { Label = Optional<string?>.Present("b") }
        ).ShouldBeFalse();
    }

    [Test]
    public void OptionalWrapperPreservesPresence()
    {
        var fragment = new Settings.Fragment { Enabled = Optional<bool>.Present(false) };

        Settings.Patch
            .Between(
                Optional<Settings.Fragment?>.Present(fragment),
                Optional<Settings.Fragment?>.Present(fragment)
            )
            .IsEmpty.ShouldBeTrue();
        Settings.Patch
            .Between(
                Optional<Settings.Fragment?>.Present(fragment),
                Optional<Settings.Fragment?>.Missing
            )
            .IsEmpty.ShouldBeFalse();
        Settings.Patch
            .Between(Optional<Settings.Fragment?>.Missing, Optional<Settings.Fragment?>.Missing)
            .IsEmpty.ShouldBeTrue();
    }

    private static bool AreEqual(Settings.Fragment? left, Settings.Fragment? right) =>
        Settings.Patch
            .Between(
                left is null ? Optional<Settings.Fragment?>.Missing : Optional<Settings.Fragment?>.Present(left),
                right is null ? Optional<Settings.Fragment?>.Missing : Optional<Settings.Fragment?>.Present(right)
            )
            .IsEmpty;

    private static bool DictionaryAreEqual(
        DictionarySettings.Fragment left,
        DictionarySettings.Fragment right
    ) =>
        DictionarySettings.Patch
            .Between(
                Optional<DictionarySettings.Fragment?>.Present(left),
                Optional<DictionarySettings.Fragment?>.Present(right)
            )
            .IsEmpty;

    private static bool SetAreEqual(SetSettings.Fragment left, SetSettings.Fragment right) =>
        SetSettings.Patch
            .Between(
                Optional<SetSettings.Fragment?>.Present(left),
                Optional<SetSettings.Fragment?>.Present(right)
            )
            .IsEmpty;
}
