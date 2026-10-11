namespace SparseFragments.Tests;

public sealed class SparsePathValueTests
{
    [Test]
    public void DefaultPathsHaveStableRootIdentity()
    {
        var path = default(SparsePath);
        path.ShouldBe(SparsePath.Root<object>());
        path.GetHashCode().ShouldBe(SparsePath.Root<object>().GetHashCode());
        path.IsRoot.ShouldBeTrue();
        path.Depth.ShouldBe(0);
        path.Segments.Count.ShouldBe(0);
        path.Parent.ShouldBeNull();
        path.ToString().ShouldBe("$root");
        path.Member("Name").Parent.ShouldBe(path);

        var typed = default(SparsePath<Settings, string>);
        typed.Path.ShouldBe(SparsePath.Root<Settings>());
        typed.ShouldBe(new SparsePath<Settings, string>(SparsePath.Root<Settings>()));
        typed.GetHashCode().ShouldBe(typed.Path.GetHashCode());
        SparsePath converted = typed;
        converted.RootType.ShouldBe(typeof(Settings));
        new Dictionary<SparsePath, int> { [path] = 1 }[SparsePath.Root<object>()]
            .ShouldBe(1);
    }

    [Test]
    public void DefaultBuildersRetainTheirRootAndCollection()
    {
        var builder = default(Settings.SparsePaths<Settings>);
        builder.Label.Path.ShouldBe(Settings.SparsePath.Label.Path);
        SparsePath root = builder;
        root.ShouldBe(SparsePath.Root<Settings>());

        var items = default(KeyedServerHolder.SparsePaths<KeyedServerHolder>.SparseItemsPaths);
        items.Key("a").Name.Path.ShouldBe(KeyedServerHolder.SparsePath.Items.Key("a").Name.Path);
        SparsePath collection = items;
        collection.ShouldBe(SparsePath.Root<KeyedServerHolder>().Member("Items"));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void PublicConstructorsKeepCallerArraysDetached(bool enumerable)
    {
        SparsePathSegment[] segments = [SparsePathSegment.Member("Name")];
        var path = enumerable
            ? new SparsePath(typeof(Settings), (IEnumerable<SparsePathSegment>)segments)
            : new SparsePath(typeof(Settings), segments);
        var child = path.Member("Child");
        var prefixed = path.Prepend(SparsePath.Root<Settings>().Member("Parent"));

        segments[0] = SparsePathSegment.Member("Changed");

        path.ToString().ShouldBe("Name");
        child.ToString().ShouldBe("Name.Child");
        child.Parent.ShouldBe(path);
        prefixed.ToString().ShouldBe("Parent.Name");
        Should.Throw<NotSupportedException>(() =>
            ((IList<SparsePathSegment>)path.Segments)[0] = SparsePathSegment.Member("Changed")
        );
    }
}
