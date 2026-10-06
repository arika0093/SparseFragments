using System.ComponentModel;

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class ObservableChild
{
    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }
}

[SparseFragmentModel]
public partial struct ObservableSpot
{
    public int X { get; set; }

    public int Y { get; set; }
}

[SparseFragmentModel]
public partial class ObservableHolder
{
    public string Title { get; set; } = string.Empty;

    public string? Note { get; set; }

    public ObservableChild Child { get; set; } = new();

    public ObservableChild? MaybeChild { get; set; }

    public ObservableSpot Spot { get; set; }

    public List<string> Tags { get; set; } = new();

    public int OwningCount { get; init; }
}

[SparseFragmentModel]
public partial class ObservableNode
{
    public int Value { get; set; }

    public ObservableNode? Next { get; set; }
}

[SparseFragmentModel]
public partial class ObservableCollision
{
    public string Observable { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string PropertyChanged { get; set; } = string.Empty;
}

public sealed class ObservableTests
{
    private static List<string> Events(INotifyPropertyChanged source)
    {
        var names = new List<string>();
        source.PropertyChanged += (_, args) => names.Add(args.PropertyName ?? "<null>");
        return names;
    }

    [Test]
    public void ScalarGetSetAndNotification()
    {
        var model = new ObservableHolder { Title = "a" };
        var proxy = new ObservableHolder.Observable(model);
        var names = Events(proxy);

        proxy.Title.ShouldBe("a");
        proxy.Title = "b";
        model.Title.ShouldBe("b");
        names.ShouldBe(["Title"]);
    }

    [Test]
    public void EqualitySuppression()
    {
        var model = new ObservableHolder { Title = "a" };
        var proxy = new ObservableHolder.Observable(model);
        var names = Events(proxy);

        proxy.Title = "a";
        names.ShouldBeEmpty();
        model.Title = "b";
        proxy.Note = null;
        names.ShouldBeEmpty();
    }

    [Test]
    public void NestedProxyCachingAndPropagation()
    {
        var notified = 0;
        var model = new ObservableHolder { Child = new ObservableChild { Name = "n" } };
        var proxy = new ObservableHolder.Observable(model, () => notified++);

        ReferenceEquals(proxy.Child, proxy.Child).ShouldBeTrue();
        var names = Events(proxy);

        proxy.Child!.Name = "n2";
        model.Child.Name.ShouldBe("n2");
        notified.ShouldBe(1);
        names.ShouldBe(["Child"]);
    }

    [Test]
    public void NestedReplacementRebuildsProxy()
    {
        var notified = 0;
        var model = new ObservableHolder { Child = new ObservableChild { Name = "old" } };
        var proxy = new ObservableHolder.Observable(model, () => notified++);
        var names = Events(proxy);

        var first = proxy.Child;
        proxy.Child = new ObservableChild.Observable(new ObservableChild { Name = "new" });
        model.Child.Name.ShouldBe("new");
        ReferenceEquals(first, proxy.Child).ShouldBeFalse();
        proxy.Child!.Name.ShouldBe("new");
        names.ShouldBe(["Child"]);
        notified.ShouldBe(1);
    }

    [Test]
    public void NullNestedPassthrough()
    {
        var model = new ObservableHolder { MaybeChild = null };
        var proxy = new ObservableHolder.Observable(model);

        proxy.MaybeChild.ShouldBeNull();

        proxy.MaybeChild = new ObservableChild.Observable(new ObservableChild { Name = "x" });
        model.MaybeChild!.Name.ShouldBe("x");
        proxy.MaybeChild!.Name.ShouldBe("x");

        proxy.MaybeChild = null;
        model.MaybeChild.ShouldBeNull();
        proxy.MaybeChild.ShouldBeNull();
    }

    [Test]
    public void InitAndReadOnlyMembers()
    {
        var model = new ObservableHolder { OwningCount = 3, Title = "t" };
        var proxy = new ObservableHolder.Observable(model);

        proxy.OwningCount.ShouldBe(3);
        proxy.Title = "t2";
        model.Title.ShouldBe("t2");
    }

    [Test]
    public void ValueTypeStructuralMember()
    {
        var model = new ObservableHolder { Spot = new ObservableSpot { X = 1, Y = 2 } };
        var proxy = new ObservableHolder.Observable(model);
        var names = Events(proxy);

        proxy.Spot.X.ShouldBe(1);
        proxy.Spot = new ObservableSpot { X = 9, Y = 2 };
        model.Spot.X.ShouldBe(9);
        names.ShouldBe(["Spot"]);
    }

    [Test]
    public void CollectionReplacement()
    {
        var model = new ObservableHolder { Tags = new() { "a" } };
        var proxy = new ObservableHolder.Observable(model);
        var names = Events(proxy);

        var same = model.Tags;
        proxy.Tags = same;
        names.ShouldBeEmpty();

        proxy.Tags = new() { "b" };
        model.Tags.ShouldBe(["b"]);
        names.ShouldBe(["Tags"]);
    }

    [Test]
    public void RecursiveNesting()
    {
        var notified = 0;
        var model = new ObservableNode
        {
            Value = 1,
            Next = new ObservableNode { Value = 2 },
        };
        var proxy = new ObservableNode.Observable(model, () => notified++);

        proxy.Next!.Value = 20;
        model.Next!.Value.ShouldBe(20);
        notified.ShouldBe(1);
    }

    [Test]
    public void GeneratedNameCollisions()
    {
        var model = new ObservableCollision
        {
            Observable = "o",
            Model = "m",
            PropertyChanged = "p",
        };
        var proxy = new ObservableCollision.SparseObservable(model);
        var names = Events(proxy);

        proxy.Observable.ShouldBe("o");
        proxy.Model.ShouldBe("m");
        proxy.Observable = "o2";
        model.Observable.ShouldBe("o2");
        names.ShouldBe(["Observable"]);
    }

    [Test]
    public void NullableMembers()
    {
        var model = new ObservableHolder { Note = "x" };
        var proxy = new ObservableHolder.Observable(model);
        var names = Events(proxy);

        proxy.Note = null;
        model.Note.ShouldBeNull();
        names.ShouldBe(["Note"]);
    }
}
