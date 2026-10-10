using SparseFragments;

namespace SparseFragments.Tests;

/// <summary>Path-based rebase conflict resolution (issue #203).</summary>
/// <remarks>
/// Generic consumer coverage: every test below addresses conflicts through the
/// canonical <see cref="SparsePath"/> (generated <c>T.SparsePath</c> fluent
/// where typed, <see cref="SparsePath"/> factories otherwise). No per-model
/// conflict members and no UI framework types appear.
/// </remarks>
public sealed class RebaseResolutionTests
{
    private static ResOrder Order(
        string number = "n",
        decimal price = 1m,
        string? note = "a",
        int? retry = null
    ) =>
        new()
        {
            Number = number,
            Price = price,
            Note = note,
            Retry = retry,
        };

    private static Optional<ResOrder.Fragment?> OrderState(ResOrder model) =>
        Optional<ResOrder.Fragment?>.Present(ResOrder.Fragment.From(model));

    private static string Describe(IEnumerable<SparseConflict> conflicts) =>
        string.Join(";", conflicts.Select(static conflict => conflict.PathText));

    private static string DescribePaths(IEnumerable<SparsePath> paths) =>
        string.Join(";", paths.Select(static path => path.ToString()));

    private static SparsePath MissingOrderPath() => SparsePath.Root<ResOrder>().Member("Missing");

    [Test]
    public void Enumerate_Find_And_Query_By_Typed_Path()
    {
        var baseModel = Order(price: 1m, note: "a");
        var edited = Order(price: 2m, note: "b");
        var current = Order(price: 3m, note: "c");

        var rebase = baseModel.CreateChangeSet(edited).RebaseOnto(OrderState(current));
        rebase.HasConflicts.ShouldBeTrue();
        var resolution = ResOrder.ChangeSet.BeginResolution(rebase, current);

        resolution.EnumerateConflicts().Count.ShouldBe(2);
        resolution.EnumerateUnresolvedConflicts().Count.ShouldBe(2);
        resolution.DecidedCount.ShouldBe(0);

        var price = ResOrder.SparsePath.Price;
        SparsePath untyped = price;
        untyped.ToString().ShouldBe("Price");

        resolution.FindConflict(price).ShouldNotBeNull();
        resolution.HasConflict(price).ShouldBeTrue();
        resolution.HasConflict(MissingOrderPath()).ShouldBeFalse();
        resolution.FindConflict(MissingOrderPath()).ShouldBeNull();

        // Ancestor/descendant queries around one nested-style check on flat members.
        resolution.HasConflictsUnder(SparsePath.Root<ResOrder>()).ShouldBeTrue();
        resolution.HasConflictsUnder(price).ShouldBeFalse();
        resolution.HasConflictsAffecting(price).ShouldBeTrue();
        resolution.HasConflictsAffecting(MissingOrderPath()).ShouldBeFalse();

        resolution.GetState(price).ShouldBe(SparseConflictState.Unresolved);
        resolution.GetState(MissingOrderPath()).ShouldBe(SparseConflictState.NoConflict);

        resolution.UseIncoming(price);
        resolution.GetState(price).ShouldBe(SparseConflictState.ResolvedUseIncoming);
        resolution.EnumerateUnresolvedConflicts().Count.ShouldBe(1);
        resolution.DecidedCount.ShouldBe(1);
    }

    [Test]
    public void Incoming_Current_And_Custom_Values_Build()
    {
        var baseModel = Order(price: 1m, note: "a");
        var edited = Order(price: 2m, note: "b");
        var current = Order(price: 3m, note: "c");

        var rebase = baseModel.CreateChangeSet(edited).RebaseOnto(OrderState(current));
        var resolution = new ResOrder.ChangeSet.Resolution(rebase, OrderState(current));

        resolution.UseIncoming(ResOrder.SparsePath.Price);
        resolution.UseCurrent(ResOrder.SparsePath.Note);
        var built = resolution.TryBuild(out var resolved, out var failure);
        built.ShouldBeTrue($"failure: {failure?.Reason}");
        failure.ShouldBeNull();
        resolved.ShouldNotBeNull();

        var applied = resolved!.ToPatch().Apply(OrderState(current));
        applied.IsPresent.ShouldBeTrue();
        var model = applied.Value!.ToModel();
        model.Price.ShouldBe(2m);
        model.Note.ShouldBe("c");
    }

    [Test]
    public void Custom_Value_Overrides_Both_Sides()
    {
        var baseModel = Order(price: 1m);
        var edited = Order(price: 2m);
        var current = Order(price: 3m);

        var rebase = baseModel.CreateChangeSet(edited).RebaseOnto(OrderState(current));
        var resolution = ResOrder.ChangeSet.BeginResolution(rebase, current);

        resolution.SetValue(ResOrder.SparsePath.Price, 1200m);
        resolution.GetState(ResOrder.SparsePath.Price).ShouldBe(SparseConflictState.ResolvedCustom);
        resolution.TryBuild(out var resolved, out _).ShouldBeTrue();

        var model = resolved!.ToPatch().Apply(OrderState(current)).Value!.ToModel();
        model.Price.ShouldBe(1200m);
    }

    [Test]
    public void Multiple_Independent_Conflicts_Preserve_Clean_Edits()
    {
        var baseModel = Order(price: 1m, note: "a", retry: 1);
        var edited = Order(price: 2m, note: "b", retry: 2);
        var current = Order(price: 3m, note: "c", retry: 1);

        var rebase = baseModel.CreateChangeSet(edited).RebaseOnto(OrderState(current));
        Describe(rebase.Conflicts).ShouldBe("Note;Price");
        var resolution = ResOrder.ChangeSet.BeginResolution(rebase, current);

        resolution.UseIncoming(ResOrder.SparsePath.Price);
        resolution.UseCurrent(ResOrder.SparsePath.Note);
        resolution
            .TryBuild(out var resolved, out var failure)
            .ShouldBeTrue($"failure: {failure?.Reason}");

        // Retry was a clean rebased edit; it must survive alongside both resolutions.
        var model = resolved!.ToPatch().Apply(OrderState(current)).Value!.ToModel();
        model.Price.ShouldBe(2m);
        model.Note.ShouldBe("c");
        model.Retry.ShouldBe(2);
    }

    [Test]
    public void TryBuild_Rejects_Unresolved_Without_Mutating_Inputs()
    {
        var baseModel = Order(price: 1m, note: "a");
        var edited = Order(price: 2m, note: "b");
        var current = Order(price: 3m, note: "c");
        var changes = baseModel.CreateChangeSet(edited);

        var beforeChanges = DescribePaths(changes.EnumerateChangedPaths());
        var rebase = changes.RebaseOnto(OrderState(current));
        var resolution = ResOrder.ChangeSet.BeginResolution(rebase, current);

        resolution.UseIncoming(ResOrder.SparsePath.Price);
        resolution.TryBuild(out var resolved, out var failure).ShouldBeFalse();
        resolved.ShouldBeNull();
        failure.ShouldNotBeNull();
        failure!.UnresolvedConflicts.Count.ShouldBe(1);
        failure.UnresolvedConflicts[0].PathText.ShouldBe("Note");

        // Neither the original change set nor the input models changed.
        DescribePaths(changes.EnumerateChangedPaths()).ShouldBe(beforeChanges);
        current.Price.ShouldBe(3m);
        current.Note.ShouldBe("c");
        edited.Price.ShouldBe(2m);
    }

    [Test]
    public void Unknown_And_Ancestor_Custom_Paths_Are_Rejected()
    {
        var baseModel = Order(price: 1m);
        var edited = Order(price: 2m);
        var current = Order(price: 3m);

        var rebase = baseModel.CreateChangeSet(edited).RebaseOnto(OrderState(current));
        var resolution = ResOrder.ChangeSet.BeginResolution(rebase, current);

        Should.Throw<InvalidOperationException>(() => resolution.UseIncoming(MissingOrderPath()));
        Should.Throw<InvalidOperationException>(() =>
            resolution.Remove(SparsePath.Root<ResOrder>())
        );
        resolution.ClearDecision(MissingOrderPath()).ShouldBeFalse();
    }

    [Test]
    public void Presence_Operations_Below_Member_Granularity_Are_Rejected()
    {
        var baseLabels = new ResLabels
        {
            Labels = new HashSet<string> { "x", "y" },
        };
        var edited = new ResLabels { Labels = new HashSet<string> { "y" } };
        var current = new ResLabels
        {
            Labels = new HashSet<string> { "x", "y", "z" },
        };
        Optional<ResLabels.Fragment?> State(ResLabels model) =>
            Optional<ResLabels.Fragment?>.Present(ResLabels.Fragment.From(model));

        // Set conflicts stay member-level, so element presence operations have no
        // exact conflict path and must fail fast instead of silently succeeding.
        var rebase = baseLabels.CreateChangeSet(edited).RebaseOnto(State(current));
        var resolution = ResLabels.ChangeSet.BeginResolution(rebase, current);
        SparsePath element = ResLabels.SparsePath.Labels.Element("x");
        Should.Throw<InvalidOperationException>(() => resolution.Remove(element));
        Should.Throw<InvalidOperationException>(() =>
            resolution.SetValue(ResLabels.SparsePath.Labels.Element("x"), "x")
        );

        // Whole-member removal is exact and builds.
        SparsePath member = ResLabels.SparsePath.Labels;
        resolution.Remove(member);
        resolution
            .TryBuild(out var resolved, out var failure)
            .ShouldBeTrue($"failure: {failure?.Reason}");
        var entry = resolved!.EnumerateChanges().Single();
        entry.Kind.ShouldBe(ResLabels.ChangeSet.ChangeKind.Removed);
    }

    [Test]
    public void Present_Null_And_Missing_Stay_Distinct()
    {
        var baseModel = Order(note: "a");
        var edited = Order(note: null);
        var current = Order(note: "b");

        var nullRebase = baseModel.CreateChangeSet(edited).RebaseOnto(OrderState(current));
        var nullResolution = ResOrder.ChangeSet.BeginResolution(nullRebase, current);
        nullResolution.SetValue(ResOrder.SparsePath.Note, Optional<string?>.Present(null));
        nullResolution.TryBuild(out var nullResolved, out _).ShouldBeTrue();
        var nullEntry = nullResolved!.Find(ResOrder.SparsePath.Note);
        nullEntry.ShouldNotBeNull();
        nullEntry!.Kind.ShouldBe(ResOrder.ChangeSet.ChangeKind.Changed);
        nullEntry.After.IsPresent.ShouldBeTrue();
        nullEntry.After.Value.ShouldBeNull();

        var missingRebase = baseModel.CreateChangeSet(edited).RebaseOnto(OrderState(current));
        var missingResolution = ResOrder.ChangeSet.BeginResolution(missingRebase, current);
        SparsePath note = ResOrder.SparsePath.Note;
        missingResolution.Remove(note);
        missingResolution.TryBuild(out var missingResolved, out _).ShouldBeTrue();
        var missingEntry = missingResolved!.Find(ResOrder.SparsePath.Note);
        missingEntry.ShouldNotBeNull();
        missingEntry!.Kind.ShouldBe(ResOrder.ChangeSet.ChangeKind.Removed);
        missingEntry.After.IsPresent.ShouldBeFalse();
    }

    [Test]
    public void Nested_Leaf_And_Ancestor_Resolution()
    {
        var baseRoot = new ResNestedRoot
        {
            Label = "a",
            Customer = new ResCustomer { Name = "Ann" },
        };
        var edited = new ResNestedRoot
        {
            Label = "b",
            Customer = new ResCustomer { Name = "Bob" },
        };
        var current = new ResNestedRoot
        {
            Label = "c",
            Customer = new ResCustomer { Name = "Cat" },
        };
        Optional<ResNestedRoot.Fragment?> State(ResNestedRoot model) =>
            Optional<ResNestedRoot.Fragment?>.Present(ResNestedRoot.Fragment.From(model));

        var rebase = baseRoot.CreateChangeSet(edited).RebaseOnto(State(current));
        Describe(rebase.Conflicts).ShouldBe("Customer.Name;Label");
        var resolution = ResNestedRoot.ChangeSet.BeginResolution(rebase, current);

        SparsePath customer = ResNestedRoot.SparsePath.Customer;
        var customerName = ResNestedRoot.SparsePath.Customer.Name;
        resolution.HasConflict(customer).ShouldBeFalse();
        resolution.HasConflictsUnder(customer).ShouldBeTrue();
        resolution.HasConflictsAffecting(customer).ShouldBeTrue();

        // Ancestor UseIncoming fans out to the leaf below it.
        resolution.UseIncoming(customer);
        resolution.UseCurrent(ResNestedRoot.SparsePath.Label);
        resolution
            .TryBuild(out var resolved, out var failure)
            .ShouldBeTrue($"failure: {failure?.Reason}");

        var model = resolved!.ToPatch().Apply(State(current)).Value!.ToModel();
        model.Label.ShouldBe("c");
        model.Customer!.Name.ShouldBe("Bob");

        // Exact decisions win over ancestors; ancestor customs are disallowed.
        var second = ResNestedRoot.ChangeSet.BeginResolution(rebase, current);
        second.UseCurrent(customer);
        second.UseCurrent(ResNestedRoot.SparsePath.Label);
        second.UseIncoming(ResNestedRoot.SparsePath.Customer.Name);
        var wholeCustomer = new SparsePath<ResNestedRoot, ResCustomer?>(
            ResNestedRoot.SparsePath.Customer
        );
        Should.Throw<InvalidOperationException>(() =>
            second.SetValue(wholeCustomer, new ResCustomer { Name = "Dan" })
        );
        second.TryBuild(out var secondResolved, out _).ShouldBeTrue();
        secondResolved!
            .ToPatch()
            .Apply(State(current))
            .Value!.ToModel()
            .Customer!.Name.ShouldBe("Bob");
    }

    [Test]
    public void Keyed_Deletion_Versus_Child_Edit()
    {
        var baseRoster = new ResRoster
        {
            Items = new()
            {
                new ResItem { Id = "a", Price = 1m },
                new ResItem { Id = "b", Price = 2m },
            },
        };
        var edited = new ResRoster
        {
            Items = new()
            {
                new ResItem { Id = "b", Price = 20m },
            },
        };
        var current = new ResRoster
        {
            Items = new()
            {
                new ResItem { Id = "a", Price = 10m },
                new ResItem { Id = "b", Price = 30m },
            },
        };
        Optional<ResRoster.Fragment?> State(ResRoster model) =>
            Optional<ResRoster.Fragment?>.Present(ResRoster.Fragment.From(model));

        var rebase = baseRoster.CreateChangeSet(edited).RebaseOnto(State(current));
        var paths = Describe(rebase.Conflicts);
        paths.ShouldBe(
            "Items[\"b\"].Price;Items[\"a\"]",
            $"keyed conflicts carry element identity, got: {paths}"
        );
        var resolution = ResRoster.ChangeSet.BeginResolution(rebase, current);

        // Deletion wins for a; current wins for the b leaf.
        SparsePath deleted = ResRoster.SparsePath.Items.Key("a");
        resolution.UseIncoming(deleted);
        resolution.UseCurrent(ResRoster.SparsePath.Items.Key("b").Price);
        resolution
            .TryBuild(out var resolved, out var failure)
            .ShouldBeTrue($"failure: {failure?.Reason}");

        var model = resolved!.ToPatch().Apply(State(current)).Value!.ToModel();
        model.Items.Count.ShouldBe(1);
        model.Items[0].Id.ShouldBe("b");
        model.Items[0].Price.ShouldBe(30m);

        // Member-level UseIncoming covers both element conflicts at once.
        var member = ResRoster.ChangeSet.BeginResolution(rebase, current);
        SparsePath items = ResRoster.SparsePath.Items;
        member.UseIncoming(items);
        member.TryBuild(out var memberResolved, out _).ShouldBeTrue();
        var memberModel = memberResolved!.ToPatch().Apply(State(current)).Value!.ToModel();
        memberModel.Items.Count.ShouldBe(1);
        memberModel.Items[0].Id.ShouldBe("b");
        memberModel.Items[0].Price.ShouldBe(20m);
    }

    [Test]
    public void Dictionary_Entry_Set_And_Remove()
    {
        var baseDict = new ResDict
        {
            Scores = new() { ["a"] = 1, ["b"] = 2 },
        };
        var edited = new ResDict { Scores = new() { ["a"] = 10 } };
        var current = new ResDict
        {
            Scores = new() { ["a"] = 100, ["b"] = 20 },
        };
        Optional<ResDict.Fragment?> State(ResDict model) =>
            Optional<ResDict.Fragment?>.Present(ResDict.Fragment.From(model));

        var rebase = baseDict.CreateChangeSet(edited).RebaseOnto(State(current));
        var paths = Describe(rebase.Conflicts);
        paths.ShouldBe("Scores[\"b\"];Scores[\"a\"]", $"dict entry paths, got: {paths}");
        var resolution = ResDict.ChangeSet.BeginResolution(rebase, current);

        resolution.UseIncoming(ResDict.SparsePath.Scores.Key("a"));
        resolution.UseIncoming(ResDict.SparsePath.Scores.Key("b"));
        resolution
            .TryBuild(out var resolved, out var failure)
            .ShouldBeTrue($"failure: {failure?.Reason}");

        var model = resolved!.ToPatch().Apply(State(current)).Value!.ToModel();
        model.Scores.Count.ShouldBe(1);
        model.Scores["a"].ShouldBe(10);

        // Custom entry values are statically typed.
        var custom = ResDict.ChangeSet.BeginResolution(rebase, current);
        custom.SetValue(ResDict.SparsePath.Scores.Key("a"), 42);
        custom.UseCurrent(ResDict.SparsePath.Scores.Key("b"));
        custom.TryBuild(out var customResolved, out _).ShouldBeTrue();
        var customModel = customResolved!.ToPatch().Apply(State(current)).Value!.ToModel();
        customModel.Scores["a"].ShouldBe(42);
        customModel.Scores["b"].ShouldBe(20);
    }

    [Test]
    public void Set_Member_Resolves_As_A_Whole()
    {
        var baseLabels = new ResLabels
        {
            Labels = new HashSet<string> { "x", "y" },
        };
        var edited = new ResLabels { Labels = new HashSet<string> { "y" } };
        var current = new ResLabels
        {
            Labels = new HashSet<string> { "x", "y", "z" },
        };
        Optional<ResLabels.Fragment?> State(ResLabels model) =>
            Optional<ResLabels.Fragment?>.Present(ResLabels.Fragment.From(model));

        var rebase = baseLabels.CreateChangeSet(edited).RebaseOnto(State(current));
        var paths = Describe(rebase.Conflicts);
        paths.ShouldBe("Labels", $"set conflicts stay member-level, got: {paths}");
        var resolution = ResLabels.ChangeSet.BeginResolution(rebase, current);

        SparsePath labels = ResLabels.SparsePath.Labels;
        resolution.UseIncoming(labels);
        resolution
            .TryBuild(out var resolved, out var failure)
            .ShouldBeTrue($"failure: {failure?.Reason}");

        var model = resolved!.ToPatch().Apply(State(current)).Value!.ToModel();
        model.Labels.SetEquals(new[] { "y" }).ShouldBeTrue();
    }

    [Test]
    public void Keyed_Reorder_Conflict_Resolves_By_Member_Path()
    {
        var baseRoster = new ResRoster
        {
            Items = new()
            {
                new ResItem { Id = "a", Price = 1m },
                new ResItem { Id = "b", Price = 2m },
            },
        };
        var edited = new ResRoster
        {
            Items = new()
            {
                new ResItem { Id = "b", Price = 2m },
                new ResItem { Id = "a", Price = 1m },
            },
        };
        var current = new ResRoster
        {
            Items = new()
            {
                new ResItem { Id = "a", Price = 1m },
                new ResItem { Id = "b", Price = 2m },
                new ResItem { Id = "c", Price = 3m },
            },
        };
        Optional<ResRoster.Fragment?> State(ResRoster model) =>
            Optional<ResRoster.Fragment?>.Present(ResRoster.Fragment.From(model));

        var rebase = baseRoster.CreateChangeSet(edited).RebaseOnto(State(current));
        var paths = Describe(rebase.Conflicts);
        paths.ShouldBe("Items", $"order conflict path, got: {paths}");
        var resolution = ResRoster.ChangeSet.BeginResolution(rebase, current);

        SparsePath items = ResRoster.SparsePath.Items;
        resolution.UseIncoming(items);
        resolution
            .TryBuild(out var resolved, out var failure)
            .ShouldBeTrue($"failure: {failure?.Reason}");

        var model = resolved!.ToPatch().Apply(State(current)).Value!.ToModel();
        model.Items.Select(static item => item.Id).ShouldBe(["b", "a", "c"]);
    }

    [Test]
    public void Root_Presence_Conflict_Resolves()
    {
        var baseModel = Order(price: 1m);
        var edited = Order(price: 2m);
        var current = Order(price: 3m);

        var rebase = baseModel
            .CreateChangeSet(edited)
            .RebaseOnto(Optional<ResOrder.Fragment?>.Missing);
        rebase.HasConflicts.ShouldBeTrue();
        rebase.Conflicts.Count.ShouldBe(1);
        rebase.Conflicts[0].Kind.ShouldBe(SparseConflictKind.WholeContribution);
        rebase.Conflicts[0].Path.IsRoot.ShouldBeTrue();

        var resolution = ResOrder.ChangeSet.BeginResolution(
            rebase,
            Optional<ResOrder.Fragment?>.Missing
        );
        resolution.HasConflict(SparsePath.Root<ResOrder>()).ShouldBeTrue();
        resolution.GetState(SparsePath.Root<ResOrder>()).ShouldBe(SparseConflictState.Unresolved);

        resolution.UseCurrent(SparsePath.Root<ResOrder>());
        resolution
            .TryBuild(out var resolved, out var failure)
            .ShouldBeTrue($"failure: {failure?.Reason}");
        resolved!.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void Redacted_Conflicts_Keep_Current_And_Hide_Plaintext()
    {
        var options = new ChangePayloadRebaseOptions
        {
            RejectChangesWithRedactedBeforeValuesDuringRebase = true,
            RedactedBeforePaths = ["Password"],
        };
        var baseSecret = new ResSecret { DisplayName = "a", Password = "p1" };
        var edited = new ResSecret { DisplayName = "b", Password = "p2" };
        var current = new ResSecret { DisplayName = "a", Password = "c" };
        Optional<ResSecret.Fragment?> State(ResSecret model) =>
            Optional<ResSecret.Fragment?>.Present(ResSecret.Fragment.From(model));

        var rebase = baseSecret.CreateChangeSet(edited).RebaseOnto(State(current), options);
        rebase.HasConflicts.ShouldBeTrue();
        var conflict = rebase.Conflicts.Single();
        conflict.Kind.ShouldBe(SparseConflictKind.RedactedBefore);
        conflict.PathText.ShouldBe("Password");
        conflict.BaseValue.IsPresent.ShouldBeFalse();
        conflict.LocalValue.IsPresent.ShouldBeFalse();
        conflict.CurrentValue.IsPresent.ShouldBeFalse();
        (conflict.Reason ?? string.Empty).ShouldNotContain("p2");

        var password = ResSecret.SparsePath.Password;
        var resolution = ResSecret.ChangeSet.BeginResolution(rebase, current);
        Should.Throw<InvalidOperationException>(() => resolution.UseIncoming(password));
        Should.Throw<InvalidOperationException>(() => resolution.SetValue(password, "x"));
        resolution.UseCurrent(password);

        resolution
            .TryBuild(out var resolved, out var failure)
            .ShouldBeTrue($"failure: {failure?.Reason}");
        var model = resolved!.ToPatch().Apply(State(current)).Value!.ToModel();
        model.DisplayName.ShouldBe("b");
        model.Password.ShouldBe("c");

        var unresolved = ResSecret.ChangeSet.BeginResolution(rebase, current);
        unresolved.TryBuild(out _, out var redactedFailure).ShouldBeFalse();
        redactedFailure!.UnresolvedConflicts.Count.ShouldBe(1);
    }

    [Test]
    public void Already_Applied_Rebase_Builds_Empty()
    {
        var baseModel = Order(price: 1m);
        var edited = Order(price: 2m);
        var current = Order(price: 2m);

        var rebase = baseModel.CreateChangeSet(edited).RebaseOnto(OrderState(current));
        rebase.HasConflicts.ShouldBeFalse();
        var resolution = ResOrder.ChangeSet.BeginResolution(rebase, current);
        resolution.EnumerateConflicts().Count.ShouldBe(0);
        resolution.EnumerateUnresolvedConflicts().Count.ShouldBe(0);

        resolution.TryBuild(out var resolved, out _).ShouldBeTrue();
        resolved!.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void Resolved_ChangeSet_Reapplies_Against_Newer_State()
    {
        var baseModel = Order(number: "n", price: 1m);
        var edited = Order(number: "n", price: 2m);
        var current = Order(number: "n", price: 3m);

        var rebase = baseModel.CreateChangeSet(edited).RebaseOnto(OrderState(current));
        var resolution = ResOrder.ChangeSet.BeginResolution(rebase, current);
        resolution.UseIncoming(ResOrder.SparsePath.Price);
        resolution.TryBuild(out var resolved, out _).ShouldBeTrue();

        var newer = Order(number: "n2", price: 3m);
        resolved!.TryApplyTo(newer, out var updated, out var conflicts).ShouldBeTrue();
        conflicts.ShouldBeNull();
        updated!.Price.ShouldBe(2m);
        updated.Number.ShouldBe("n2");
    }

    [Test]
    public void Resolution_Against_Changed_State_Fails_Fast()
    {
        var baseModel = Order(price: 1m, note: "a");
        var edited = Order(price: 2m, note: "b");
        var currentA = Order(price: 3m, note: "a");
        // currentB moved the cleanly-rebased member, so the stored rebased
        // edits no longer replay onto it.
        var currentB = Order(price: 3m, note: "z");

        var rebase = baseModel.CreateChangeSet(edited).RebaseOnto(OrderState(currentA));
        var resolution = ResOrder.ChangeSet.BeginResolution(rebase, currentB);
        resolution.UseIncoming(ResOrder.SparsePath.Price);

        resolution.TryBuild(out var resolved, out var failure).ShouldBeFalse();
        resolved.ShouldBeNull();
        failure.ShouldNotBeNull();
        currentB.Price.ShouldBe(3m);
        currentB.Note.ShouldBe("z");
    }

    [Test]
    public void Generic_Consumer_Sees_State_Without_Model_Members()
    {
        var baseModel = Order(price: 1m, note: "a");
        var edited = Order(price: 2m, note: "b");
        var current = Order(price: 3m, note: "c");

        var rebase = baseModel.CreateChangeSet(edited).RebaseOnto(OrderState(current));
        var resolution = ResOrder.ChangeSet.BeginResolution(rebase, current);

        foreach (var conflict in resolution.EnumerateConflicts())
        {
            resolution.GetState(conflict.Path).ShouldBe(SparseConflictState.Unresolved);
        }

        foreach (var conflict in resolution.EnumerateUnresolvedConflicts())
        {
            resolution.UseIncoming(conflict.Path);
        }

        foreach (var conflict in resolution.EnumerateConflicts())
        {
            resolution.GetState(conflict.Path).ShouldNotBe(SparseConflictState.Unresolved);
        }

        resolution.TryBuild(out var resolved, out _).ShouldBeTrue();
        var model = resolved!.ToPatch().Apply(OrderState(current)).Value!.ToModel();
        model.Price.ShouldBe(2m);
        model.Note.ShouldBe("b");
    }
}
