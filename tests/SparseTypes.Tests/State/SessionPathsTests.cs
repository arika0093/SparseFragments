using SparseTypes;

namespace SparseTypes.Tests;

// Issue #270: session-owned typed path navigation.
[SparseModel]
public partial class SessionPathsCustomer
{
    public string Name { get; set; } = string.Empty;
}

[SparseModel]
public partial class SessionPathsItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
}

[SparseModel]
public partial class SessionPathsTempItem
{
    [SparseKey(Unassigned = 0)]
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }
}

[SparseModel]
public partial class SessionPathsOrder
{
    public SessionPathsCustomer Customer { get; set; } = new();

    public SessionPathsCustomer? MaybeCustomer { get; set; }

    public string? Note { get; set; }

    public List<SessionPathsItem> Items { get; set; } = new();

    public List<SessionPathsTempItem> Lines { get; set; } = new();

    public Dictionary<string, int> Scores { get; set; } = new();

    public Dictionary<string, SessionPathsItem> Details { get; set; } = new();

    public ISet<string> Tags { get; set; } = new HashSet<string>();
}

[SparseModel]
public partial class SessionPathsReadonly
{
    public SessionPathsReadonly(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public int Count { get; init; }
}

/// <summary>Session-owned typed path navigation (issue #270).</summary>
public sealed class SessionPathsTests
{
    private static string ReadRootPath<TModel, TPaths>(
        ISparseEditSessionWithPaths<TModel, TPaths> session
    )
        where TModel : class
        where TPaths : ISparseTypedPath<TModel, TModel>
    {
        return session.Paths.TypedPath.Path.ToString();
    }

    [Test]
    public void SessionPathsMatchModelRootNavigation()
    {
        var session = new SessionPathsOrder().CreateEditSession();

        SparsePath<SessionPathsOrder, string> name = session.Paths.Customer.Name;
        SparsePath<SessionPathsOrder, string> title = session.Paths.Items.Key("a").Title;
        SparsePath<SessionPathsOrder, string> positional = session.Paths.Items.At(0).Title;

        name.ToString().ShouldBe("Customer.Name");
        title.ToString().ShouldBe("Items[\"a\"].Title");
        positional.ToString().ShouldBe("Items[0].Title");

        // Same navigation as the model root builder, both typed and untyped.
        SparsePath root = session.Paths;
        root.ShouldBe(SparsePath.Root<SessionPathsOrder>());
        root.ShouldBe((SparsePath)SessionPathsOrder.SparsePath);
        session.Paths.UntypedPath.ShouldBe(SessionPathsOrder.SparsePath.UntypedPath);
        session.Paths.TypedPath.ShouldBe(SessionPathsOrder.SparsePath.TypedPath);
        session.Paths.CanWriteModelValueInPlace.ShouldBeTrue();
    }

    [Test]
    public void GenericConsumerKeepsConcreteBuilderWithoutReflection()
    {
        var session = new SessionPathsOrder().CreateEditSession();

        // Type arguments are inferred from the session; the test never names
        // the relocated generated builder type.
        ReadRootPath(session).ShouldBe("$root");

        ISparseEditSession<SessionPathsOrder> fallback = session;
        fallback.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void SessionPathsDriveFieldStateAndObservation()
    {
        var model = new SessionPathsOrder
        {
            Items = [new SessionPathsItem { Id = "a", Title = "before" }],
        };
        var session = model.CreateEditSession();
        session.Observable.Items.First(item => item.Id == "a").Title = "after";

        var title = session.FieldState(session.Paths.Items.Key("a").Title);
        title.IsChanged.ShouldBeTrue();
        title.Before.Value.ShouldBe("before");
        title.After.Value.ShouldBe("after");

        using var observed = session.ObserveField(session.Paths.Customer.Name);
        observed.HasChanges.ShouldBeFalse();

        var scores = session.FieldState(session.Paths.Scores.Key("missing"));
        scores.HasChanges.ShouldBeFalse();

        // Added rows surface through the session collection state.
        session.Observable.Items.AddModel(new SessionPathsItem { Id = "b", Title = "new" });
        var added = session.CollectionState(session.Paths.Items).GetItemState("b");
        added.IsAdded.ShouldBeTrue();
    }

    [Test]
    public void CollectionBuildersExposeTypedValueContract()
    {
        var session = new SessionPathsOrder().CreateEditSession();

        ISparseTypedPath<SessionPathsOrder, List<SessionPathsItem>> items = session.Paths.Items;
        items.UntypedPath.ShouldBe(SparsePath.Root<SessionPathsOrder>().Member("Items"));
        items.TypedPath.Path.ShouldBe(items.UntypedPath);
        items.CanWriteModelValueInPlace.ShouldBeFalse();
        Should.Throw<NotSupportedException>(() =>
            items.WriteModelValueInPlace(new List<SessionPathsItem>(), new List<SessionPathsItem>())
        );

        ISparseTypedPath<SessionPathsOrder, Dictionary<string, int>> scores = session.Paths.Scores;
        scores.UntypedPath.ShouldBe(SparsePath.Root<SessionPathsOrder>().Member("Scores"));

        ISparseTypedPath<SessionPathsOrder, Dictionary<string, SessionPathsItem>> details = session
            .Paths
            .Details;
        details.UntypedPath.ShouldBe(SparsePath.Root<SessionPathsOrder>().Member("Details"));

        ISparseTypedPath<SessionPathsOrder, ISet<string>> tags = session.Paths.Tags;
        tags.UntypedPath.ShouldBe(SparsePath.Root<SessionPathsOrder>().Member("Tags"));

        // Scalar leaves and nested builders keep their existing contracts.
        ISparseTypedPath<SessionPathsOrder, string?> note = session.Paths.Note;
        note.UntypedPath.ShouldBe(SparsePath.Root<SessionPathsOrder>().Member("Note"));
        ISparseTypedPath<SessionPathsOrder, SessionPathsCustomer> customer = session.Paths.Customer;
        customer.CanWriteModelValueInPlace.ShouldBeTrue();

        // Element navigation still resolves through the collection builders.
        SparsePath<SessionPathsOrder, int> score = session.Paths.Scores.Key("k");
        score.ToString().ShouldBe("Scores[\"k\"]");
        SparsePath<SessionPathsOrder, string> detail = session.Paths.Details.Key("k").Title;
        detail.ToString().ShouldBe("Details[\"k\"].Title");
        SparsePath<SessionPathsOrder, string> tag = session.Paths.Tags.Element("t");
        tag.ToString().ShouldBe("Tags[\"t\"]");
    }

    [Test]
    public void MissingReplacedAndTemporaryEntriesNavigateStructurally()
    {
        var model = new SessionPathsOrder
        {
            Items = [new SessionPathsItem { Id = "a", Title = "kept" }],
        };
        var session = model.CreateEditSession();

        session.Paths.Items.Key("missing").Title.ToString().ShouldBe("Items[\"missing\"].Title");
        session.FieldState(session.Paths.Items.Key("missing").Title).HasChanges.ShouldBeFalse();

        model.Customer = new SessionPathsCustomer { Name = "replaced" };
        session.FieldState(session.Paths.Customer.Name).IsChanged.ShouldBeTrue();

        var temporary = Guid.NewGuid();
        session.Observable.Lines.AddModel(
            new SessionPathsTempItem { Title = "pending", TemporaryId = temporary }
        );
        session
            .Paths.Lines.TemporaryKey(temporary)
            .Title.ToString()
            .ShouldBe("Lines[temp:\"" + temporary.ToString("D") + "\"].Title");
        session
            .CollectionState(session.Paths.Lines)
            .GetTemporaryState(temporary)
            .IsAdded.ShouldBeTrue();
    }

    [Test]
    public void NullableAndReadonlyMembersNavigate()
    {
        var model = new SessionPathsOrder();
        var session = model.CreateEditSession();

        SparsePath<SessionPathsOrder, string?> note = session.Paths.Note;
        note.ToString().ShouldBe("Note");
        session.FieldState(session.Paths.Note).HasChanges.ShouldBeFalse();

        model.MaybeCustomer = new SessionPathsCustomer { Name = "added" };
        // Null-to-object replacement carries no leaf records: the leaf sees
        // the ancestor change, mirroring the Settings.Nested contract.
        var addedLeaf = session.FieldState(session.Paths.MaybeCustomer.Name);
        addedLeaf.IsChanged.ShouldBeFalse();
        addedLeaf.HasAncestorChange.ShouldBeTrue();

        var readonlySession = new SessionPathsReadonly("base").CreateEditSession();
        SparsePath<SessionPathsReadonly, string> name = readonlySession.Paths.Name;
        SparsePath<SessionPathsReadonly, int> count = readonlySession.Paths.Count;
        name.ToString().ShouldBe("Name");
        count.ToString().ShouldBe("Count");
        readonlySession.FieldState(readonlySession.Paths.Name).HasChanges.ShouldBeFalse();
    }

    [Test]
    public void PathsAccessDoesNotReadModelOrNotify()
    {
        var session = new SessionPathsOrder().CreateEditSession();
        var notifications = 0;
        session.PropertyChanged += (_, _) => notifications++;

        session.HasChanges.ShouldBeFalse();
        for (var index = 0; index < 10; index++)
        {
            _ = session.Paths.Customer.Name;
            _ = session.Paths.Items.Key("a").Title;
        }

        notifications.ShouldBe(0);
        session.HasChanges.ShouldBeFalse();

        // Editing still notifies exactly once per batch after path-only access.
        session.Observable.Note = "edited";
        notifications.ShouldBeGreaterThan(0);
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void ChildScopeKeepsMinimalContract()
    {
        var session = new SessionPathsOrder().CreateEditSession();

        using var scope = session.OpenScope<SessionPathsCustomer>(session.Paths.Customer);

        (scope is ISparseEditSession<SessionPathsCustomer>).ShouldBeTrue();
        // A generic child scope cannot promise the root's concrete builder.
        (
            (object)scope
            is ISparseEditSessionWithPaths<
                SessionPathsCustomer,
                ISparseTypedPath<SessionPathsCustomer, SessionPathsCustomer>
            >
        ).ShouldBeFalse();
    }
}
