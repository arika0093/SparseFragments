namespace SparseFragments.Tests;

public sealed class DescriptorKeyedIdentityTests
{
    [Test]
    public void SingleKeyMetadataAndLookupSurviveReorder()
    {
        var model = new KeyedServerHolder
        {
            Items =
            [
                new KeyedServer { Id = "a", Name = "a" },
                new KeyedServer { Id = "b", Name = "b" },
            ],
        };
        var session = model.CreateEditSession();

        session.Descriptors.TryGet(nameof(KeyedServerHolder.Items), out var items).ShouldBeTrue();
        var array = items.Array.ShouldNotBeNull();
        array!.IsKeyed.ShouldBeTrue();
        array.KeyType.ShouldBe(typeof(string));
        array.KeyPropertyNames.ShouldBe(["Id"]);
        array.IsUnassignedKey("a").ShouldBeFalse();
        array.GetItemKey(0).ShouldBe("a");
        array.GetItemKey(1).ShouldBe("b");
        array.IndexOfKey("b").ShouldBe(1);
        array.IndexOfKey("missing").ShouldBe(-1);
        array.IndexOfKey(null).ShouldBe(-1);
        array.IndexOfKey(42).ShouldBe(-1);

        session.Observable.Items!.Move(0, 1);

        // Positional paths moved, but key identity is stable.
        array.IndexOfKey("a").ShouldBe(1);
        array.IndexOfKey("b").ShouldBe(0);
        array.GetItemKey(0).ShouldBe("b");
    }

    [Test]
    public void ReplacementUpdatesKeyLookup()
    {
        var model = new KeyedServerHolder { Items = [new KeyedServer { Id = "a", Name = "a" }] };
        var session = model.CreateEditSession();

        session.Descriptors.TryGet(nameof(KeyedServerHolder.Items), out var items).ShouldBeTrue();
        var array = items.Array.ShouldNotBeNull();

        array!.TrySetItem(0, new KeyedServer { Id = "c", Name = "c" }).ShouldBeTrue();

        array.IndexOfKey("a").ShouldBe(-1);
        array.IndexOfKey("c").ShouldBe(0);
        array.GetItemKey(0).ShouldBe("c");
    }

    [Test]
    public void CompositeKeysExposeOrderedNamesAndTupleLookup()
    {
        var model = new CompositeServerHolder
        {
            Items =
            [
                new CompositeServer
                {
                    TenantId = "t",
                    Id = "i",
                    Name = "n",
                },
            ],
        };
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(CompositeServerHolder.Items), out var items)
            .ShouldBeTrue();
        var array = items.Array.ShouldNotBeNull();
        array!.IsKeyed.ShouldBeTrue();
        array.KeyType.ShouldBe(typeof(ValueTuple<string, string>));
        array.KeyPropertyNames.ShouldBe(["TenantId", "Id"]);
        array.GetItemKey(0).ShouldBe(("t", "i"));
        array.IndexOfKey(("t", "i")).ShouldBe(0);
        array.IndexOfKey(("t", "missing")).ShouldBe(-1);
        array.IsUnassignedKey(("t", "i")).ShouldBeFalse();
    }

    [Test]
    public void UnassignedSentinelsAreDiscoverableAndIndependent()
    {
        // Unassigned entries are arranged without session notifications: live
        // keyed transitions cannot represent them (session keyed validation
        // rejects unassigned baselines, as for direct observable mutation).
        var model = new AssignedServerHolder
        {
            Items =
            [
                new AssignedServer { Id = 7, Name = "assigned" },
                new AssignedServer { Id = 0, Name = "first" },
                new AssignedServer { Id = 0, Name = "second" },
            ],
        };
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(AssignedServerHolder.Items), out var items)
            .ShouldBeTrue();
        var array = items.Array.ShouldNotBeNull();
        array!.IsKeyed.ShouldBeTrue();
        array.KeyType.ShouldBe(typeof(int));
        array.KeyPropertyNames.ShouldBe(["Id"]);
        array.IsUnassignedKey(0).ShouldBeTrue();
        array.IsUnassignedKey(7).ShouldBeFalse();
        array.IsUnassignedKey("nope").ShouldBeFalse();

        // Pure-equality lookup finds the first sentinel; callers exempt via IsUnassignedKey.
        array.GetItemKey(0).ShouldBe(7);
        array.GetItemKey(1).ShouldBe(0);
        array.GetItemKey(2).ShouldBe(0);
        array.IndexOfKey(0).ShouldBe(1);
        array.IndexOfKey(7).ShouldBe(0);
    }

    [Test]
    public void ScalarSequencesReportUnkeyed()
    {
        var model = new ScalarSequenceHolder { Tags = ["a"] };
        var session = model.CreateEditSession();

        session.Descriptors.TryGet(nameof(ScalarSequenceHolder.Tags), out var tags).ShouldBeTrue();
        var array = tags.Array.ShouldNotBeNull();
        array!.IsKeyed.ShouldBeFalse();
        array.KeyType.ShouldBeNull();
        array.KeyPropertyNames.ShouldBeEmpty();
        array.GetItemKey(0).ShouldBeNull();
        array.IndexOfKey("a").ShouldBe(-1);
        array.IsUnassignedKey("a").ShouldBeFalse();
    }
}
