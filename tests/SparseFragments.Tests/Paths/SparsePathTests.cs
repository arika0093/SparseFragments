namespace SparseFragments.Tests;

/// <summary>Runtime contracts of the canonical typed path abstraction.</summary>
public sealed class SparsePathTests
{
    [Test]
    public void RootMemberKeyAndIndexSegmentsEnumerateInOrder()
    {
        var path = SparsePath.Root<Settings>().Member("Nested").Member("Host");
        path.IsRoot.ShouldBeFalse();
        path.Depth.ShouldBe(2);
        path.RootType.ShouldBe(typeof(Settings));
        path.Segments.Count.ShouldBe(2);
        path.Segments[0].ShouldBe(SparsePathSegment.Member("Nested"));
        path.Segments[1].Kind.ShouldBe(SparsePathSegmentKind.Member);

        var keyed = SparsePath.Root<Settings>().Member("Items").Key("a").Member("Name");
        keyed.Segments[1].Kind.ShouldBe(SparsePathSegmentKind.Key);
        keyed.Segments[1].Key.ShouldBe("a");
        keyed.Segments[1].KeyType.ShouldBe(typeof(string));

        var indexed = SparsePath.Root<Settings>().Member("Plugins").At(2);
        indexed.Segments[1].ShouldBe(SparsePathSegment.At(2));
        indexed.Segments[1].Index.ShouldBe(2);
    }

    [Test]
    public void IdentityDistinguishesRootsKindsNamesAndTypedKeys()
    {
        var label = SparsePath.Root<Settings>().Member("Label");
        label.ShouldBe(SparsePath.Root<Settings>().Member("Label"));
        label.GetHashCode().ShouldBe(SparsePath.Root<Settings>().Member("Label").GetHashCode());

        label.ShouldNotBe(SparsePath.Root<Nested>().Member("Label"));
        label.ShouldNotBe(SparsePath.Root<Settings>().Member("RetryCount"));
        label.ShouldNotBe(SparsePath.Root<Settings>().At(0));

        var intKey = SparsePath.Root<Settings>().Member("M").Key(42);
        var stringKey = SparsePath.Root<Settings>().Member("M").Key("42");
        intKey.ShouldNotBe(stringKey);

        var guid = Guid.NewGuid();
        SparsePath
            .Root<Settings>()
            .Member("M")
            .Key(guid)
            .ShouldBe(SparsePath.Root<Settings>().Member("M").Key(guid));

        var first = SparsePath.Root<Settings>().Member("M").Key((1, "a"));
        var second = SparsePath.Root<Settings>().Member("M").Key((1, "a"));
        var other = SparsePath.Root<Settings>().Member("M").Key((1, "b"));
        first.ShouldBe(second);
        first.ShouldNotBe(other);
    }

    [Test]
    public void ParentAncestorsAndContainmentFollowSegments()
    {
        var root = SparsePath.Root<Settings>();
        root.Parent.ShouldBeNull();
        root.IsRoot.ShouldBeTrue();

        var nested = root.Member("Nested");
        var host = nested.Member("Host");
        host.Parent.ShouldBe(nested);
        nested.Parent.ShouldBe(root);

        nested.IsAncestorOf(host).ShouldBeTrue();
        host.IsDescendantOf(nested).ShouldBeTrue();
        host.IsAncestorOf(host).ShouldBeFalse();
        host.StartsWith(nested).ShouldBeTrue();
        host.StartsWith(host).ShouldBeTrue();
        nested.StartsWith(host).ShouldBeFalse();

        // Different roots never contain each other.
        var foreign = SparsePath.Root<Nested>().Member("Host");
        nested.IsAncestorOf(foreign).ShouldBeFalse();
        host.StartsWith(SparsePath.Root<Nested>()).ShouldBeFalse();
    }

    [Test]
    public void WireTextKeepsTheHistoricalGrammar()
    {
        SparsePath.Root<Settings>().ToString().ShouldBe("$root");
        SparsePath.Root<Settings>().Member("Label").ToString().ShouldBe("Label");
        SparsePath
            .Root<Settings>()
            .Member("Nested")
            .Member("Host")
            .ToString()
            .ShouldBe("Nested.Host");
        SparsePath.Root<Settings>().Member("Items").Key("a").ToString().ShouldBe("Items[\"a\"]");
        SparsePath.Root<Settings>().Member("Plugins").At(1).ToString().ShouldBe("Plugins[1]");
        SparsePath.Root<Settings>().Member("Scores").Key(42).ToString().ShouldBe("Scores[\"42\"]");
    }

    [Test]
    public void KeyTextEscapesForDisplayAndTransport()
    {
        var tricky = "a\nb\tc\"d\\e";
        var path = SparsePath.Root<Settings>().Member("Scores").Key(tricky);
        var text = path.ToString();
        foreach (var c in text)
        {
            (c < 0x20).ShouldBeFalse();
        }

        // The quoted text parses back to the original key.
        var parsed = SparsePath.Parse<Settings>(text);
        parsed.Segments.Count.ShouldBe(2);
        parsed.Segments[1].Key.ShouldBe(tricky);
    }

    [Test]
    public void ParseRoundTripsMemberKeyAndIndexSegments()
    {
        var paths = new[]
        {
            SparsePath.Root<Settings>(),
            SparsePath.Root<Settings>().Member("Label"),
            SparsePath.Root<Settings>().Member("Nested").Member("Host"),
            SparsePath.Root<Settings>().Member("Items").Key("a").Member("Name"),
            SparsePath.Root<Settings>().Member("Plugins").At(3),
        };
        foreach (var path in paths)
        {
            SparsePath.Parse(typeof(Settings), path.ToString()).ShouldBe(path);
        }

        // Equal display text never merges distinct typed keys: the parsed
        // "42" is a string key, not the original int key.
        var intKey = SparsePath.Root<Settings>().Member("Scores").Key(42);
        var parsedIntKey = SparsePath.Parse(typeof(Settings), intKey.ToString());
        parsedIntKey.ToString().ShouldBe(intKey.ToString());
        parsedIntKey.ShouldNotBe(intKey);
        parsedIntKey.ShouldBe(SparsePath.Root<Settings>().Member("Scores").Key("42"));

        SparsePath.TryParse<Settings>("not a path..", out _).ShouldBeFalse();
        SparsePath.TryParse<Settings>("A[", out _).ShouldBeFalse();
        SparsePath.TryParse<Settings>(null, out var missing).ShouldBeFalse();
        missing.ShouldBeNull();
    }

    [Test]
    public void TypedPathsConvertToTheModelAgnosticForm()
    {
        SparsePath<Settings, string?> label = Settings.SparsePath.Label;
        label.RootType.ShouldBe(typeof(Settings));
        label.ValueType.ShouldBe(typeof(string));
        SparsePath untyped = label;
        untyped.ShouldBe(SparsePath.Root<Settings>().Member("Label"));
        label.ToString().ShouldBe("Label");

        SparsePath<KeyedServerHolder, string> name = KeyedServerHolder
            .SparsePath.Items.Key("a")
            .Name;
        name.ValueType.ShouldBe(typeof(string));
        name.Path.RootType.ShouldBe(typeof(KeyedServerHolder));
    }

    [Test]
    public void TypedPathsRejectForeignRoots()
    {
        var foreign = SparsePath.Root<Nested>().Member("Host");
        Should.Throw<ArgumentException>(() => new SparsePath<Settings, string>(foreign));
        Should.Throw<ArgumentNullException>(() => new SparsePath<Settings, string>(null!));
    }
}
