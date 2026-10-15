using SparseTypes.Generated;

namespace SparseTypes.Tests;

/// <summary>UI-neutral change-row projection over flattened records.</summary>
public sealed class SparseChangeRowProjectionTests
{
    private static KeyedServerHolder Holder() =>
        new()
        {
            Items = new()
            {
                new KeyedServer
                {
                    Id = "a",
                    Name = "alpha",
                    Count = 1,
                },
                new KeyedServer
                {
                    Id = "b",
                    Name = "beta",
                    Count = 2,
                },
            },
        };

    [Test]
    public void ScalarEditProjectsTypedEndpointsAndFormattedText()
    {
        var session = new Settings().CreateEditSession();
        session.Observable.RetryCount = 4;

        var rows = SparseChangeRowProjection.Project(
            session.CreateChangeSet(),
            new SparseChangeRowOptions { FormatValue = static value => $"[{value}]" }
        );

        rows.ShouldHaveSingleItem();
        var row = rows[0];
        row.PathText.ShouldBe(nameof(Settings.RetryCount));
        row.Path.ToString().ShouldBe(nameof(Settings.RetryCount));
        row.Kind.ShouldBe(SparseChangeKind.Changed);
        row.Operation.ShouldBe(nameof(SparseChangeKind.Changed));
        row.Before.IsPresent.ShouldBeTrue();
        row.Before.Value.ShouldBe(3);
        row.After.Value.ShouldBe(4);
        row.BeforeText.ShouldBe("[3]");
        row.AfterText.ShouldBe("[4]");
    }

    [Test]
    public void NullableNullStaysDistinctFromMissing()
    {
        var session = new Settings().CreateEditSession();
        session.Observable.Label = null;

        var rows = SparseChangeRowProjection.Project(
            session.CreateChangeSet(),
            new SparseChangeRowOptions
            {
                FormatValue = static value => value is null ? "null" : value.ToString()!,
            }
        );

        // Present(null) reaches the hook as null and stays distinct from
        // Missing.
        var nulled = rows.ShouldHaveSingleItem();
        nulled.PathText.ShouldBe(nameof(Settings.Label));
        nulled.Kind.ShouldBe(SparseChangeKind.Changed);
        nulled.After.IsPresent.ShouldBeTrue();
        nulled.After.Value.ShouldBeNull();
        nulled.AfterText.ShouldBe("null");

        // A removed keyed entry reports a Missing after endpoint, never null.
        var holder = Holder().CreateEditSession();
        holder.Observable.Items.RemoveAt(0);
        var removed = SparseChangeRowProjection
            .Project(holder.CreateChangeSet())
            .Single(row => row.PathText == "Items[\"a\"]");
        removed.Kind.ShouldBe(SparseChangeKind.Removed);
        removed.Before.IsPresent.ShouldBeTrue();
        removed.After.IsPresent.ShouldBeFalse();
        removed.AfterText.ShouldBe("—");
        removed.BeforeText.ShouldNotBe("—");
    }

    [Test]
    public void NestedEditProjectsDottedPath()
    {
        var session = new Settings().CreateEditSession();
        session.Observable.Nested!.Host = "example";

        var rows = SparseChangeRowProjection.Project(session.CreateChangeSet());

        var row = rows.ShouldHaveSingleItem();
        row.PathText.ShouldBe("Nested.Host");
        row.Kind.ShouldBe(SparseChangeKind.Changed);
        row.Before.Value.ShouldBe("localhost");
        row.After.Value.ShouldBe("example");
        // Null hooks fall back to ToString so tables never render type names.
        row.BeforeText.ShouldBe("localhost");
        row.AfterText.ShouldBe("example");
    }

    [Test]
    public void KeyedAddAndRemoveKeepStableTypedEndpoints()
    {
        var session = Holder().CreateEditSession();
        session.Observable.Items.RemoveAt(0);
        session.Observable.Items.AddModel(
            new KeyedServer
            {
                Id = "c",
                Name = "gamma",
                Count = 3,
            }
        );

        var rows = SparseChangeRowProjection.Project(session.CreateChangeSet());

        // Removing the head shifts the surviving rank, so the set also
        // reports its key-order transition alongside the entry rows.
        rows.Count.ShouldBe(3);
        var added = rows.Single(row => row.PathText == "Items[\"c\"]");
        added.Kind.ShouldBe(SparseChangeKind.Added);
        added.Operation.ShouldBe(nameof(SparseChangeKind.Added));
        added.Before.IsPresent.ShouldBeFalse();
        added.BeforeText.ShouldBe("—");
        // The whole element stays typed until formatting: tables can show a
        // summary while callers still read the stable key off the value.
        added.After.Value.ShouldBeOfType<KeyedServer>().Id.ShouldBe("c");

        var removed = rows.Single(row => row.PathText == "Items[\"a\"]");
        removed.Kind.ShouldBe(SparseChangeKind.Removed);
        removed.After.IsPresent.ShouldBeFalse();
        removed.AfterText.ShouldBe("—");
        removed.Before.Value.ShouldBeOfType<KeyedServer>().Id.ShouldBe("a");

        // Order rows keep the typed key lists on both endpoints.
        var order = rows.Single(row => row.Kind == SparseChangeKind.Order);
        order.PathText.ShouldBe(nameof(KeyedServerHolder.Items));
        ((IReadOnlyList<string>)order.Before.Value!).ShouldBe(["a", "b"]);
        ((IReadOnlyList<string>)order.After.Value!).ShouldBe(["b", "c"]);
    }

    [Test]
    public void KeyedLeafEditProjectsLeafPath()
    {
        var session = Holder().CreateEditSession();
        session.Observable.Items[1].Count = 3;

        var rows = SparseChangeRowProjection.Project(session.CreateChangeSet());

        var row = rows.ShouldHaveSingleItem();
        // Keyed entries report stable key identity, not positions.
        row.PathText.ShouldBe("Items[\"b\"].Count");
        row.Kind.ShouldBe(SparseChangeKind.Changed);
        row.Before.Value.ShouldBe(2);
        row.After.Value.ShouldBe(3);
        row.BeforeText.ShouldBe("2");
        row.AfterText.ShouldBe("3");
    }

    [Test]
    public void ReorderProjectsOrderOperation()
    {
        var session = Holder().CreateEditSession();
        session.Observable.Items.Move(1, 0);

        var rows = SparseChangeRowProjection.Project(session.CreateChangeSet());

        var order = rows.Single(row => row.Kind == SparseChangeKind.Order);
        order.PathText.ShouldBe(nameof(KeyedServerHolder.Items));
        order.Operation.ShouldBe(nameof(SparseChangeKind.Order));
    }

    [Test]
    public void ThrowingFormattersFallBackWithoutThrowing()
    {
        var session = new Settings().CreateEditSession();
        session.Observable.RetryCount = 4;

        var rows = SparseChangeRowProjection.Project(
            session.CreateChangeSet(),
            new SparseChangeRowOptions
            {
                FormatValue = static _ => throw new InvalidOperationException("boom"),
            }
        );

        var row = rows.ShouldHaveSingleItem();
        row.BeforeText.ShouldBe("Error: InvalidOperationException: boom");
        row.AfterText.ShouldBe("Error: InvalidOperationException: boom");
        // Typed endpoints survive even when their text cannot render.
        row.Before.Value.ShouldBe(3);

        var custom = SparseChangeRowProjection.Project(
            session.CreateChangeSet(),
            new SparseChangeRowOptions
            {
                FormatValue = static _ => throw new InvalidOperationException("boom"),
                FormatError = static exception => "bad:" + exception.GetType().Name,
            }
        );
        custom[0].BeforeText.ShouldBe("bad:InvalidOperationException");

        var operation = SparseChangeRowProjection.Project(
            session.CreateChangeSet(),
            new SparseChangeRowOptions
            {
                FormatOperation = static _ => throw new InvalidOperationException("labels broke"),
            }
        );
        operation[0].Operation.ShouldBe("Error: InvalidOperationException: labels broke");
    }

    [Test]
    public void CustomOperationMissingAndErrorHooksAreHonored()
    {
        var session = Holder().CreateEditSession();
        session.Observable.Items.RemoveAt(0);

        var rows = SparseChangeRowProjection.Project(
            session.CreateChangeSet(),
            new SparseChangeRowOptions
            {
                FormatOperation = static kind => "op-" + kind,
                MissingText = "(absent)",
            }
        );

        var row = rows.Single(r => r.Kind == SparseChangeKind.Removed);
        row.PathText.ShouldBe("Items[\"a\"]");
        row.Operation.ShouldBe("op-Removed");
        row.AfterText.ShouldBe("(absent)");

        rows.Single(r => r.Kind == SparseChangeKind.Order).Operation.ShouldBe("op-Order");
    }

    [Test]
    public void RecordsOverloadPreservesCallerOrder()
    {
        var session = Holder().CreateEditSession();
        session.Observable.Items.RemoveAt(0);
        session.Observable.Items.AddModel(
            new KeyedServer
            {
                Id = "c",
                Name = "gamma",
                Count = 3,
            }
        );

        var records = ((ISparseChangeSet)session.CreateChangeSet()).EnumerateChangeRecords();
        records.Count.ShouldBe(3);

        var rows = SparseChangeRowProjection.Project(records.AsEnumerable().Reverse().ToArray());
        rows.Select(static row => row.PathText)
            .ShouldBe(records.Select(static record => record.PathText).Reverse().ToArray());
    }

    [Test]
    public void NullInputsThrowAndEmptyProjectsEmpty()
    {
        Should.Throw<ArgumentNullException>(() =>
            SparseChangeRowProjection.Project((ISparseChangeSet)null!)
        );
        Should.Throw<ArgumentNullException>(() =>
            SparseChangeRowProjection.Project((IReadOnlyList<SparseChangeRecord>)null!)
        );
        Should.Throw<ArgumentException>(() =>
            SparseChangeRowProjection.Project(new List<SparseChangeRecord> { null! })
        );

        var session = new Settings().CreateEditSession();
        SparseChangeRowProjection.Project(session.CreateChangeSet()).ShouldBeEmpty();
    }
}
