using System.Reflection;

namespace SparseFragments.Tests;

/// <summary>
/// Guards the <see cref="Optional{T}"/> core invariant (issue #8):
/// an optional is either Missing or Present(value), and presence cannot be
/// mutated independently of the value through the public API.
/// </summary>
public sealed class OptionalInvariantTests
{
    [Test]
    public void DefaultIsMissing()
    {
        default(Optional<int>).IsPresent.ShouldBeFalse();
        default(Optional<string?>).IsPresent.ShouldBeFalse();

        Optional<int>.Missing.ShouldBe(default(Optional<int>));
        Optional<string?>.Missing.ShouldBe(default(Optional<string?>));

        Optional<int>.Missing.IsPresent.ShouldBeFalse();
        Optional<int>.Missing.GetValueOrDefault(7).ShouldBe(7);
        Optional<int>.Missing.ToString().ShouldBe("Missing");
        Should.Throw<InvalidOperationException>(() => _ = Optional<int>.Missing.Value);
    }

    [Test]
    public void OnlyPresentCreatesPresentState()
    {
        Optional<int>.Present(42).IsPresent.ShouldBeTrue();
        Optional<int>.Present(42).Value.ShouldBe(42);

        Optional<string?> implicitValue = "hello";
        implicitValue.IsPresent.ShouldBeTrue();
        implicitValue.Value.ShouldBe("hello");

        Optional<string?> implicitNull = (string?)null;
        implicitNull.IsPresent.ShouldBeTrue();
        implicitNull.Value.ShouldBeNull();

        Optional<int>.Missing.IsPresent.ShouldBeFalse();
        default(Optional<int>).IsPresent.ShouldBeFalse();

        var copied = Optional<int>.Missing;
        Optional<int>.Present(1).CopyTo(ref copied);
        copied.ShouldBe(Optional<int>.Present(1));
    }

    [Test]
    public void PresentNullAndDefaultRemainValid()
    {
        var presentNull = Optional<string?>.Present(null);
        presentNull.IsPresent.ShouldBeTrue();
        presentNull.Value.ShouldBeNull();

        var presentDefault = Optional<int>.Present(default);
        presentDefault.IsPresent.ShouldBeTrue();
        presentDefault.Value.ShouldBe(0);

        Optional<int> implicitDefault = default(int);
        implicitDefault.IsPresent.ShouldBeTrue();
        implicitDefault.Value.ShouldBe(0);

        presentNull.GetValueOrDefault("fallback").ShouldBeNull();
        presentDefault.GetValueOrDefault(7).ShouldBe(0);
        presentNull.ToString().ShouldBe("Present()");
        presentDefault.ToString().ShouldBe("Present(0)");
    }

    [Test]
    public void PresenceHasNoPublicMutator()
    {
        foreach (var type in new[]
        {
            typeof(Optional<int>),
            typeof(Optional<string?>),
        })
        {
            var presence = type.GetProperty("IsPresent")!;
            presence.CanWrite.ShouldBeFalse();
            presence.SetMethod.ShouldBeNull();

            // No public instance constructor exists; construction goes through
            // Missing / Present(value) / implicit conversion only.
            type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .ShouldBeEmpty();
        }
    }

    [Test]
    public void EqualityMatchesSemanticMissingPresentModel()
    {
        Optional<int>.Missing.ShouldBe(default(Optional<int>));
        (Optional<int>.Missing == default(Optional<int>)).ShouldBeTrue();
        (Optional<int>.Missing != Optional<int>.Present(0)).ShouldBeTrue();

        // A present default value is distinct from missing.
        Optional<int>.Present(0).ShouldNotBe(Optional<int>.Missing);
        Optional<string?>.Present(null).ShouldNotBe(Optional<string?>.Missing);

        // Present values compare by value.
        Optional<int>.Present(1).ShouldBe(Optional<int>.Present(1));
        (Optional<int>.Present(1) == Optional<int>.Present(1)).ShouldBeTrue();
        (Optional<int>.Present(1) != Optional<int>.Present(2)).ShouldBeTrue();
        Optional<string?>.Present(null).ShouldBe(Optional<string?>.Present(null));
        Optional<string?>.Present("a").ShouldNotBe(Optional<string?>.Present("b"));

        // Missing never equals present, even for null/default payloads.
        Optional<string?>.Present(null).Equals(Optional<string?>.Missing).ShouldBeFalse();
        Optional<string?>.Missing.Equals((object)Optional<string?>.Present(null)).ShouldBeFalse();
        Optional<int>.Missing.Equals((object)Optional<int>.Missing).ShouldBeTrue();
        Optional<int>.Missing.Equals((object?)null).ShouldBeFalse();
        Optional<int>.Missing.Equals("not-an-optional").ShouldBeFalse();

        // Hash codes stay consistent with equality.
        Optional<int>.Missing.GetHashCode().ShouldBe(default(Optional<int>).GetHashCode());
        Optional<int>.Present(1).GetHashCode().ShouldBe(Optional<int>.Present(1).GetHashCode());
        Optional<string?>.Present(null).GetHashCode()
            .ShouldBe(Optional<string?>.Present(null).GetHashCode());
    }
}
