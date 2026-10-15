using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

// Pin the Case 4/5 collection-field patterns and Case 6 group/fork flows
// used by the playground migration.
namespace SparseTypes.Blazor.Tests;

public sealed class SparseHeadlessPlaygroundMigrationTests
{
    private static OrderDto Order() =>
        new()
        {
            Number = "ORD-1",
            Customer = new OrderCustomer { Name = "Ada", Email = "ada@example.com" },
            Lines = new()
            {
                new OrderLine
                {
                    Sku = "a",
                    Quantity = 1,
                    Price = 10m,
                },
                new OrderLine
                {
                    Sku = "b",
                    Quantity = 2,
                    Price = 20m,
                },
            },
        };

    private static EditSessionGroupRegistration<TModel, TChangeSet> Registration<
        TModel,
        TChangeSet
    >(
        ISparseEditSession<TModel, TChangeSet> session,
        Func<TChangeSet> createChangeSet,
        Func<TChangeSet, bool> isEmpty,
        Func<TChangeSet, IReadOnlyList<SparsePath>> enumerateChangedPaths
    )
        where TModel : class
        where TChangeSet : class =>
        new()
        {
            Session = session,
            CreateChangeSet = createChangeSet,
            IsEmpty = isEmpty,
            EnumerateChangedPaths = enumerateChangedPaths,
        };

    [Test]
    public async Task KeyedRowsShareOneSnapshotForAddedMovedAndEditedStates()
    {
        var live = Order();
        var session = live.CreateEditSession();
        SparseFieldContext<OrderDto, List<OrderLine>>? lines = null;
        SparseFieldContext<OrderDto, decimal>? price = null;
        var shownPrice = 0m;
        Headless.VendorField<decimal>? vendor = null;

        using var tree = await Headless.Tree.CreateAsync(
            Headless.Edit(
                session,
                static _ => { },
                edit =>
                    Headless.Field(
                        edit.Path.Lines,
                        captured => lines = captured,
                        _ =>
                            Headless.Field(
                                edit.Path.Lines.Key("b").Price,
                                nested =>
                                {
                                    price = nested;
                                    shownPrice = nested.Value;
                                },
                                nested =>
                                    builder =>
                                    {
                                        builder.OpenComponent<Headless.VendorField<decimal>>(0);
                                        builder.AddAttribute(1, "Value", nested.Value);
                                        builder.AddAttribute(
                                            2,
                                            "ValueChanged",
                                            nested.ValueChanged
                                        );
                                        builder.AddComponentReferenceCapture(
                                            3,
                                            component =>
                                                vendor = (Headless.VendorField<decimal>)component
                                        );
                                        builder.CloseComponent();
                                    }
                            )
                    )
            )
        );

        var linesField = lines.ShouldNotBeNull();
        price.ShouldNotBeNull();
        vendor.ShouldNotBeNull();
        shownPrice.ShouldBe(20m);

        // One collection view shared by all rows, exactly like the roster
        // parent capturing its snapshot once per render.
        linesField.TryGetCollectionState(out var view).ShouldBeTrue();
        view.ShouldNotBeNull().IsEmpty.ShouldBeTrue();

        session
            .EditCollection<OrderLine, string>(OrderDto.SparsePath.Lines)
            .TryAdd(
                new OrderLine
                {
                    Sku = "c",
                    Quantity = 3,
                    Price = 30m,
                },
                out _
            )
            .ShouldBeTrue();
        await tree.FlushAsync();

        linesField.TryGetCollectionState(out var addedView).ShouldBeTrue();
        var added = addedView.ShouldNotBeNull();
        added.GetItemState("c").IsAdded.ShouldBeTrue();
        added.GetItemState("a").IsEmpty.ShouldBeTrue();

        session.Observable.Lines.Move(0, 1);
        await tree.FlushAsync();

        linesField.TryGetCollectionState(out var movedView).ShouldBeTrue();
        var moved = movedView.ShouldNotBeNull();
        moved.OrderChanged.ShouldBeTrue();
        moved.GetItemState("a").IsReordered.ShouldBeTrue();
        moved.GetItemState("b").IsReordered.ShouldBeTrue();

        // Keyed member writes follow the key through the reorder, and the
        // render-scoped field state reports the edit.
        await tree.DispatchAsync(() => vendor!.ValueChanged.InvokeAsync(99m));
        await tree.FlushAsync();
        live.Lines.Single(line => line.Sku == "b").Price.ShouldBe(99m);
        shownPrice.ShouldBe(99m);
        price!.IsChanged.ShouldBeTrue();
    }

    [Test]
    public async Task TemporaryRowsEditThroughContextNavigation()
    {
        var board = new BlazorDraftBoard { Name = "Board" };
        board.Drafts.Add(new BlazorDraftLine { Id = 7, Title = "persisted" });
        var session = board.CreateEditSession();
        var temporaryId = Guid.NewGuid();
        session.Observable.Drafts.AddModel(
            new BlazorDraftLine
            {
                Id = 0,
                Title = "draft",
                TemporaryId = temporaryId,
            }
        );
        session.Observable.Drafts.Single(item => item.Id == 7).Title = "persisted v2";

        SparseFieldContext<BlazorDraftBoard, List<BlazorDraftLine>>? lines = null;
        SparseFieldContext<BlazorDraftBoard, string>? assignedTitle = null;
        SparseFieldContext<BlazorDraftBoard, string>? draftTitle = null;
        var shownAssigned = string.Empty;
        var shownDraft = string.Empty;

        using var tree = await Headless.Tree.CreateAsync(
            Headless.Edit(
                session,
                static _ => { },
                edit =>
                    Headless.Field(
                        edit.Path.Drafts,
                        captured => lines = captured,
                        captured =>
                            Headless.Field(
                                captured.Key(7, (BlazorDraftLine row) => row.Title),
                                nested =>
                                {
                                    assignedTitle = nested;
                                    shownAssigned = nested.Value;
                                }
                            )
                    )
            )
        );

        var linesField = lines.ShouldNotBeNull();
        var assigned = assignedTitle.ShouldNotBeNull();
        shownAssigned.ShouldBe("persisted v2");
        // A surviving edited member reports changed.
        assigned.IsChanged.ShouldBeTrue();

        using var draftTree = await Headless.Tree.CreateAsync(
            Headless.Edit(
                session,
                static _ => { },
                edit =>
                    Headless.Field(
                        edit.Path.Drafts,
                        static _ => { },
                        captured =>
                            Headless.Field(
                                captured.TemporaryKey(
                                    temporaryId,
                                    (BlazorDraftLine row) => row.Title
                                ),
                                nested =>
                                {
                                    draftTitle = nested;
                                    shownDraft = nested.Value;
                                }
                            )
                    )
            )
        );

        var draftField = draftTitle.ShouldNotBeNull();
        shownDraft.ShouldBe("draft");
        // Temporary-key paths expose no value expression, so vendor
        // value/callback inputs are the supported contract.
        draftField.ValueExpression.ShouldBeNull();

        await draftTree.DispatchAsync(() => draftField.ValueChanged.InvokeAsync("draft v2"));
        await draftTree.FlushAsync();
        shownDraft.ShouldBe("draft v2");
        board.Drafts.Single(item => item.TemporaryId == temporaryId).Title.ShouldBe("draft v2");
        // The row is still an addition, so the member reports the default
        // state: added rows highlight at the row, never at the cell.
        draftField.IsChanged.ShouldBeFalse();

        linesField.TryGetCollectionState(out var roster).ShouldBeTrue();
        var view = roster.ShouldNotBeNull();
        view.GetTemporaryState(temporaryId).IsAdded.ShouldBeTrue();
        view.GetItemState(7).IsEdited.ShouldBeTrue();
    }

    [Test]
    public void GroupPrepareCapturesChildSessionsImmutablyAndAcksPerSection()
    {
        var profile = Order();
        var profileSession = profile.CreateEditSession();
        var connection = new BlazorDraftBoard { Name = "Board" };
        var connectionSession = connection.CreateEditSession();
        var group = new EditSessionGroup();
        var profileHandle = group.Register(
            "profile",
            Registration<OrderDto, OrderDto.ChangeSet>(
                profileSession,
                profileSession.CreateChangeSet,
                static changes => changes.IsEmpty,
                static changes => changes.EnumerateChangedPaths()
            )
        );
        var connectionHandle = group.Register(
            "connection",
            Registration<BlazorDraftBoard, BlazorDraftBoard.ChangeSet>(
                connectionSession,
                connectionSession.CreateChangeSet,
                static changes => changes.IsEmpty,
                static changes => changes.EnumerateChangedPaths()
            )
        );

        profileSession.Observable.Number = "ORD-2";
        connectionSession.Observable.Name = "Board v2";
        var customer = profileSession.OpenChildSession(OrderDto.SparsePath.Customer);
        customer.Session.Observable.Name = "Grace";

        var submission = group.Prepare();
        submission.Sections.Count.ShouldBe(2);
        var captured = submission.Get(profileHandle);
        captured.Changes.Number.After.Value.ShouldBe("ORD-2");
        captured.Changes.Customer.Name.After.Value.ShouldBe("Grace");
        customer.HasChanges.ShouldBeFalse();

        // Post-capture edits never rewrite the captured submission.
        profileSession.Observable.Number = "ORD-3";
        captured.Changes.Number.After.Value.ShouldBe("ORD-2");
        submission.Get(connectionHandle).Changes.Name.After.Value.ShouldBe("Board v2");

        // Acknowledgement is per-section: the other section stays pending.
        group.AcceptChanges(submission, profileHandle);
        profileSession.HasChanges.ShouldBeTrue();
        connectionSession.HasChanges.ShouldBeTrue();
        var pending = profileSession.CreateChangeSet();
        pending.Number.Before.Value.ShouldBe("ORD-2");
        pending.Number.After.Value.ShouldBe("ORD-3");

        group.AcceptChanges(submission, connectionHandle);
        connectionSession.HasChanges.ShouldBeFalse();
    }

    [Test]
    public async Task ForkMergeAdoptionSwapsSessionsWithoutCrossWrites()
    {
        var live = Order();
        var session = live.CreateEditSession();
        var fork = session.Fork();
        fork.Observable.Number = "ORD-FORK";
        fork.Observable.Customer!.Name = "Grace";
        session.Observable.Tags.Add("heavy");

        session.TryMergeFrom(fork, out var merged, out var conflicts).ShouldBeTrue();
        conflicts.ShouldBeNull();
        var adopted = merged.ShouldNotBeNull();

        SparseFieldContext<OrderDto, string>? field = null;
        var shown = string.Empty;
        using var tree = await Headless.Tree.CreateAsync(
            Headless.Edit(
                session,
                static _ => { },
                edit =>
                    Headless.Field(
                        edit.Path.Number,
                        captured =>
                        {
                            field = captured;
                            shown = captured.Value;
                        }
                    )
            )
        );
        shown.ShouldBe("ORD-1");

        // Explicit adoption swaps the rendered session; the previous session
        // stops refreshing this tree and keeps its own model.
        await tree.ShowAsync(
            Headless.Edit(
                adopted,
                static _ => { },
                edit =>
                    Headless.Field(
                        edit.Path.Number,
                        captured =>
                        {
                            field = captured;
                            shown = captured.Value;
                        }
                    )
            )
        );

        shown.ShouldBe("ORD-FORK");
        field.ShouldNotBeNull();
        // The main-side tag is present in main, while the fork never observed
        // it, and the fork-side rename never leaked into main.
        live.Number.ShouldBe("ORD-1");
        live.Customer.Name.ShouldBe("Ada");
        live.Tags.ShouldContain("heavy");
        fork.CreateChangeSet()
            .EnumerateChangedPaths()
            .Select(static path => path.ToString())
            .Any(static text => text.Contains("Tags", StringComparison.Ordinal))
            .ShouldBeFalse();
        var adoptedChanges = adopted.CreateChangeSet();
        adoptedChanges.Number.After.Value.ShouldBe("ORD-FORK");
        var adoptedPaths = adoptedChanges
            .EnumerateChangedPaths()
            .Select(static path => path.ToString())
            .ToList();
        adoptedPaths.ShouldContain(static text =>
            text.Contains("Number", StringComparison.Ordinal)
        );
        adoptedPaths.ShouldContain(static text =>
            text.Contains("Customer", StringComparison.Ordinal)
        );
        adoptedPaths.ShouldContain(static text => text.Contains("Tags", StringComparison.Ordinal));
    }

    [Test]
    public void ForkMergeConflictLeavesBothSessionsUnchanged()
    {
        var live = Order();
        var session = live.CreateEditSession();
        var fork = session.Fork();
        fork.Observable.Number = "ORD-FORK";
        session.Observable.Number = "ORD-MAIN";

        session.TryMergeFrom(fork, out var merged, out var conflicts).ShouldBeFalse();
        merged.ShouldBeNull();
        conflicts.ShouldNotBeNull();
        conflicts.Count.ShouldBeGreaterThan(0);
        live.Number.ShouldBe("ORD-MAIN");
        fork.CreateChangeSet().Number.After.Value.ShouldBe("ORD-FORK");
    }
}
