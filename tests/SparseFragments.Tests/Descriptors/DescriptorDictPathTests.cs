namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class TrickyDictHolder
{
    public Dictionary<string, ObservableChild> Texts { get; set; } = new();

    [SparseMerge(MergeMode.Replace)]
    public Dictionary<int, ObservableChild> ByNumber { get; set; } = new();

    [SparseMerge(MergeMode.Replace)]
    public Dictionary<Guid, ObservableChild> ByGuid { get; set; } = new();
}

public sealed class DescriptorDictPathTests
{
    private static TrickyDictHolder SpecialModel() =>
        new()
        {
            Texts = new Dictionary<string, ObservableChild>
            {
                ["plain"] = new() { Name = "plain" },
                ["a]b"] = new() { Name = "bracket" },
                ["c.d"] = new() { Name = "dot" },
                ["e\"f"] = new() { Name = "quote" },
                ["g\\h"] = new() { Name = "backslash" },
                ["line1\nline2"] = new() { Name = "newline" },
                ["日本語"] = new() { Name = "unicode" },
                [string.Empty] = new() { Name = "empty" },
            },
        };

    [Test]
    public void ValueDescriptorPathsAreCanonicalAndEscaped()
    {
        var model = SpecialModel();
        var session = model.CreateEditSession();

        session.Descriptors.TryGet(nameof(TrickyDictHolder.Texts), out var texts).ShouldBeTrue();
        var dict = texts.Dictionary.ShouldNotBeNull();

        string Resolve(string key)
        {
            var set = dict!.GetValueDescriptors(key);
            set.ShouldNotBeNull();
            set!.TryGet(nameof(ObservableChild.Name), out var name).ShouldBeTrue();
            return name.Path;
        }

        Resolve("plain").ShouldBe("Texts[\"plain\"].Name");
        Resolve("a]b").ShouldBe("Texts[\"a]b\"].Name");
        Resolve("c.d").ShouldBe("Texts[\"c.d\"].Name");
        Resolve("e\"f").ShouldBe("Texts[\"e\\\"f\"].Name");
        Resolve("g\\h").ShouldBe("Texts[\"g\\\\h\"].Name");
        Resolve("line1\nline2").ShouldBe("Texts[\"line1\nline2\"].Name");
        Resolve("日本語").ShouldBe("Texts[\"日本語\"].Name");
        Resolve(string.Empty).ShouldBe("Texts[\"\"].Name");
    }

    [Test]
    public void NumericAndGuidKeysRenderQuotedWithoutTypePrefix()
    {
        var guid = new Guid("12345678-1234-1234-1234-1234567890ab");
        var model = new TrickyDictHolder
        {
            ByNumber = new() { [42] = new() { Name = "answer" } },
            ByGuid = new() { [guid] = new() { Name = "guid" } },
        };
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(TrickyDictHolder.ByNumber), out var byNumber)
            .ShouldBeTrue();
        var numbers = byNumber.Dictionary.ShouldNotBeNull();
        numbers!.KeyType.ShouldBe(typeof(int));
        var numberSet = numbers.GetValueDescriptors(42).ShouldNotBeNull();
        numberSet!.TryGet(nameof(ObservableChild.Name), out var numberName).ShouldBeTrue();
        numberName.Path.ShouldBe("ByNumber[\"42\"].Name");
        numbers.GetValueDescriptors("42").ShouldBeNull();

        session.Descriptors.TryGet(nameof(TrickyDictHolder.ByGuid), out var byGuid).ShouldBeTrue();
        var guids = byGuid.Dictionary.ShouldNotBeNull();
        guids!.KeyType.ShouldBe(typeof(Guid));
        var guidSet = guids.GetValueDescriptors(guid).ShouldNotBeNull();
        guidSet!.TryGet(nameof(ObservableChild.Name), out var guidName).ShouldBeTrue();
        guidName.Path.ShouldBe($"ByGuid[\"{guid:D}\"].Name");
    }

    [Test]
    public void DescriptorPathsMatchChangeSetPaths()
    {
        var before = new TrickyDictHolder { Texts = new() { ["a]b"] = new() { Name = "before" } } };
        var after = new TrickyDictHolder { Texts = new() { ["a]b"] = new() { Name = "after" } } };
        var paths = before
            .CreateChangeSet(after)
            .EnumerateChanges()
            .Select(change => change.Path)
            .ToArray();

        var session = before.CreateEditSession();
        session.Descriptors.TryGet(nameof(TrickyDictHolder.Texts), out var texts).ShouldBeTrue();
        var set = texts.Dictionary!.GetValueDescriptors("a]b").ShouldNotBeNull();
        set!.TryGet(nameof(ObservableChild.Name), out var name).ShouldBeTrue();

        paths.ShouldContain(name.Path);
        name.Path.ShouldBe("Texts[\"a]b\"].Name");
    }
}
