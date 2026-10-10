namespace SparseFragments.Tests;

/// <summary>Generated fluent navigation and path-first lookup.</summary>
public sealed class SparsePathNavigationTests
{
    [Test]
    public void FluentBuildersProduceCompileTimeTypedPaths()
    {
        SparsePath<Settings, string?> label = Settings.SparsePath.Label;
        SparsePath<Settings, int> retry = Settings.SparsePath.RetryCount;
        SparsePath<Settings, string> host = Settings.SparsePath.Nested.Host;
        SparsePath<Settings, int> port = Settings.SparsePath.Nested.Port;
        SparsePath<KeyedServerHolder, string> name = KeyedServerHolder
            .SparsePath.Items.Key("a")
            .Name;
        SparsePath<KeyedServerHolder, int> count = KeyedServerHolder
            .SparsePath.Items.Key("a")
            .Count;
        SparsePath<ScalarDictHolder, int> score = ScalarDictHolder.SparsePath.Scores.Key("edit");
        SparsePath<StructuralDictHolder, string> serverName = StructuralDictHolder
            .SparsePath.Servers.Key("server")
            .Name;
        SparsePath<KeyedServerHolder, string> positional = KeyedServerHolder
            .SparsePath.Items.At(0)
            .Name;

        label.ToString().ShouldBe("Label");
        retry.ToString().ShouldBe("RetryCount");
        host.ToString().ShouldBe("Nested.Host");
        port.ToString().ShouldBe("Nested.Port");
        name.ToString().ShouldBe("Items[\"a\"].Name");
        count.ToString().ShouldBe("Items[\"a\"].Count");
        score.ToString().ShouldBe("Scores[\"edit\"]");
        serverName.ToString().ShouldBe("Servers[\"server\"].Name");
        positional.ToString().ShouldBe("Items[0].Name");
    }

    [Test]
    public void BuildersConvertToModelAgnosticPaths()
    {
        SparsePath nested = Settings.SparsePath.Nested.Host;
        nested.ShouldBe(SparsePath.Root<Settings>().Member("Nested").Member("Host"));

        SparsePath items = KeyedServerHolder.SparsePath.Items;
        items.ShouldBe(SparsePath.Root<KeyedServerHolder>().Member("Items"));

        SparsePath entry = ScalarDictHolder.SparsePath.Scores.Key("x");
        entry.ShouldBe(SparsePath.Root<ScalarDictHolder>().Member("Scores").Key("x"));
    }

    [Test]
    public void FindLocatesScalarNestedAndCollectionEntries()
    {
        var before = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Label = Optional<string?>.Present("before"),
                Nested = Optional<Nested.Fragment?>.Present(
                    new Nested.Fragment { Host = Optional<string>.Present("a") }
                ),
            }
        );
        var after = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Label = Optional<string?>.Present("after"),
                Nested = Optional<Nested.Fragment?>.Present(
                    new Nested.Fragment { Host = Optional<string>.Present("b") }
                ),
            }
        );
        var changes = Settings.ChangeSet.Between(before, after);

        var label = changes.Find(Settings.SparsePath.Label);
        label.ShouldNotBeNull();
        label!.Before.Value.ShouldBe("before");
        label.After.Value.ShouldBe("after");

        var host = changes.Find(Settings.SparsePath.Nested.Host);
        host.ShouldNotBeNull();
        host!.After.Value.ShouldBe("b");

        // Exact paths only: the parent matches nothing when only the child changed.
        changes.Find(Settings.SparsePath.Nested).ShouldBeNull();
        changes.Find(Settings.SparsePath.RetryCount).ShouldBeNull();

        // Untyped lookup agrees with the typed one.
        changes
            .Find(SparsePath.Parse<Settings>("Nested.Host"))!
            .Path.ShouldBe(changes.Find(Settings.SparsePath.Nested.Host)!.Path);
    }

    [Test]
    public void FindLocatesKeyedDictionaryAndOrderEntries()
    {
        Optional<KeyedServerHolder.Fragment?> State(params KeyedServer[] items) =>
            Optional<KeyedServerHolder.Fragment?>.Present(
                KeyedServerHolder.Fragment.From(new KeyedServerHolder { Items = items.ToList() })
            );
        var changes = KeyedServerHolder.ChangeSet.Between(
            State(
                new KeyedServer
                {
                    Id = "b",
                    Name = "before",
                    Count = 1,
                }
            ),
            State(
                new KeyedServer
                {
                    Id = "a",
                    Name = "added",
                    Count = 3,
                },
                new KeyedServer
                {
                    Id = "b",
                    Name = "after",
                    Count = 1,
                }
            )
        );

        changes.Find(KeyedServerHolder.SparsePath.Items.Key("b").Name).ShouldNotBeNull();
        changes.Find(KeyedServerHolder.SparsePath.Items.Key("missing").Name).ShouldBeNull();

        var order = changes.Find(KeyedServerHolder.SparsePath.Items);
        order.ShouldNotBeNull();
        order!.Kind.ShouldBe(KeyedServerHolder.ChangeSet.ChangeKind.Order);

        var missingRoot = SparsePath.Root<KeyedServerHolder>().Member("Missing");
        changes.Find(missingRoot).ShouldBeNull();
        Should.Throw<ArgumentNullException>(() => changes.Find((SparsePath)null!));
    }

    [Test]
    public void EnumerateChangedPathsUsesTheCanonicalAbstraction()
    {
        var before = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment { Label = Optional<string?>.Present("a") }
        );
        var after = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment { Label = Optional<string?>.Present("b") }
        );
        var changes = Settings.ChangeSet.Between(before, after);

        var paths = changes.EnumerateChangedPaths();
        paths.ShouldHaveSingleItem();
        Settings.SparsePath.Label.Equals(paths[0]).ShouldBeTrue();
        paths[0].ShouldBe(SparsePath.Parse<Settings>("Label"));
    }

    [Test]
    public void ConflictsAreFoundByTypedPath()
    {
        Optional<Settings.Fragment?> State(string label) =>
            Optional<Settings.Fragment?>.Present(
                Settings.Fragment.From(new Settings { Label = label })
            );
        var result = Settings.ChangeSet.Between(State("a"), State("b")).RebaseOnto(State("c"));

        result.HasConflicts.ShouldBeTrue();
        result.Conflicts.Find(Settings.SparsePath.Label).ShouldNotBeNull();
        result.Conflicts.Find(Settings.SparsePath.RetryCount).ShouldBeNull();
        result.Conflicts.Find(SparsePath.Parse<Settings>("Label")).ShouldNotBeNull();
    }

    [Test]
    public void DescriptorsAreFoundByTypedPath()
    {
        var model = new Settings
        {
            Label = "label",
            Nested = new Nested { Host = "host" },
        };
        var session = model.CreateEditSession();

        var label = session.Descriptors.Find(Settings.SparsePath.Label);
        label.ShouldNotBeNull();
        label!.GetValue().ShouldBe("label");

        var host = session.Descriptors.Find(Settings.SparsePath.Nested.Host);
        host.ShouldNotBeNull();
        host!.GetValue().ShouldBe("host");

        session.Descriptors.Find(Settings.SparsePath.RetryCount).ShouldNotBeNull();
        session.Descriptors.Find(SparsePath.Root<Settings>()).ShouldBeNull();
    }
}
