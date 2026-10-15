using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace SparseTypes.Blazor.Tests;

// Cross-layer Blazor matrix over the headless renderer: generated Core
// sessions drive real components through save/reconcile sequences. Every
// scenario asserts the rendered (user-visible) values alongside the model
// contents, session baseline/current, pending ChangeSet, child states,
// history availability, and the surfaced error.
public sealed class CrossLayerBlazorMatrixTests
{
    private static RenderFragment ScopeContent<TModel>(
        ISparseEditSession<TModel> session,
        RenderFragment child
    )
        where TModel : class =>
        builder =>
        {
            builder.OpenComponent<SparseSessionScope<TModel>>(0);
            builder.AddAttribute(1, nameof(SparseSessionScope<TModel>.Session), session);
            builder.AddAttribute(2, nameof(SparseSessionScope<TModel>.ChildContent), child);
            builder.CloseComponent();
        };

    private static RenderFragment ChildWithContent<TParent, TChild, TPaths, TConflict, TSession>(
        ISparseEditSessionWithChildren<TParent, TConflict> parent,
        ISparseChildEditPath<TParent, TChild, TPaths, TSession> path,
        RenderFragment<SparseChildEditContext<TChild, TPaths, TConflict, TSession>> childContent
    )
        where TParent : class
        where TChild : class
        where TPaths : ISparseTypedPath<TChild, TChild>
        where TConflict : class
        where TSession : ISparseEditSessionWithPaths<TChild, TPaths> =>
        builder =>
        {
            builder.OpenComponent<SparseChildEdit<TParent, TChild, TPaths, TConflict, TSession>>(0);
            builder.AddAttribute(
                1,
                nameof(SparseChildEdit<TParent, TChild, TPaths, TConflict, TSession>.Path),
                path
            );
            builder.AddAttribute(
                2,
                nameof(SparseChildEdit<TParent, TChild, TPaths, TConflict, TSession>.ChildContent),
                childContent
            );
            builder.CloseComponent();
        };

    private static OrderDto OrderBaseline() =>
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

    private static OrderDto OrderCopy(OrderDto source) =>
        new()
        {
            Number = source.Number,
            Customer = new OrderCustomer
            {
                Name = source.Customer.Name,
                Email = source.Customer.Email,
            },
            Lines = source
                .Lines.Select(static line => new OrderLine
                {
                    Sku = line.Sku,
                    Quantity = line.Quantity,
                    Price = line.Price,
                })
                .ToList(),
        };

    [Test]
    public async Task ReorderPlusAuthoritativeReconcileKeepsRowCallbacksKeyed()
    {
        var live = OrderBaseline();
        var session = live.CreateEditSession();
        var history = session.EnableHistory();
        var prices = new List<SparseFieldContext<OrderDto, decimal>>();
        var shown = new Dictionary<string, decimal>();

        void CapturePrice(SparseFieldContext<OrderDto, decimal> field, string sku)
        {
            if (prices.Count == 0 || !ReferenceEquals(prices[^1], field))
            {
                prices.Add(field);
            }

            shown[sku] = field.Value;
        }

        using var tree = await Headless.Tree.CreateAsync(
            Headless.Edit(
                session,
                static _ => { },
                edit =>
                    builder =>
                    {
                        builder.AddContent(
                            0,
                            Headless.Field(
                                edit.Path.Lines.Key("a").Price,
                                field => CapturePrice(field, "a")
                            )
                        );
                        builder.AddContent(
                            1,
                            Headless.Field(
                                edit.Path.Lines.Key("b").Price,
                                field => CapturePrice(field, "b")
                            )
                        );
                    }
            )
        );

        prices.Count.ShouldBe(2);
        shown["a"].ShouldBe(10m);
        shown["b"].ShouldBe(20m);

        // The reorder happens before the save: callbacks must follow the key
        // through it, never the position.
        session.Observable.Lines.Move(0, 1);
        await tree.FlushAsync();
        shown["a"].ShouldBe(10m);
        shown["b"].ShouldBe(20m);

        var submitted = session.CreateChangeSet();
        submitted
            .TryApplyTo(OrderBaseline(), out var persisted, out var applyConflicts)
            .ShouldBeTrue();

        applyConflicts.ShouldBeNull();
        session.TryReconcile(submitted, persisted!, out var error).ShouldBeTrue();

        error.ShouldBeNull();
        await tree.FlushAsync();
        live.Lines.Select(static line => line.Sku).ShouldBe(["b", "a"]);

        // The latest callback for the moved row resolves the current keyed
        // item: the write lands on "b" wherever it sits.
        var bField = prices[^1];
        await tree.DispatchAsync(() => bField.ValueChanged.InvokeAsync(99m));
        await tree.FlushAsync();

        live.Lines.Single(line => line.Sku == "b").Price.ShouldBe(99m);
        live.Lines.Single(line => line.Sku == "a").Price.ShouldBe(10m);
        shown["b"].ShouldBe(99m);
        bField.IsChanged.ShouldBeTrue();
        session.HasChanges.ShouldBeTrue();
        history.CanUndo.ShouldBeTrue();
    }

    [Test]
    public async Task TempKeyAssignmentPreservesRowCallbacks()
    {
        var board = new BlazorDraftBoard { Name = "Board" };
        board.Drafts.Add(new BlazorDraftLine { Id = 7, Title = "persisted" });
        var session = board.CreateEditSession();
        var history = session.EnableHistory();
        var temporaryId = Guid.NewGuid();
        session.Observable.Drafts.AddModel(
            new BlazorDraftLine
            {
                Id = 0,
                Title = "draft",
                TemporaryId = temporaryId,
            }
        );

        SparseFieldContext<BlazorDraftBoard, List<BlazorDraftLine>>? lines = null;
        var titles = new List<SparseFieldContext<BlazorDraftBoard, string>>();
        var shown = string.Empty;
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
                                captured.TemporaryKey(
                                    temporaryId,
                                    (BlazorDraftLine row) => row.Title
                                ),
                                nested =>
                                {
                                    if (titles.Count == 0 || !ReferenceEquals(titles[^1], nested))
                                    {
                                        titles.Add(nested);
                                    }
                                    shown = nested.Value;
                                }
                            )
                    )
            )
        );

        shown.ShouldBe("draft");
        var submitted = session.CreateChangeSet();

        // The server assigns the permanent key: the authoritative readback
        // carries Id 8 under the same temporary identity.
        var authoritative = new BlazorDraftBoard { Name = "Board" };
        authoritative.Drafts.Add(new BlazorDraftLine { Id = 7, Title = "persisted" });
        authoritative.Drafts.Add(
            new BlazorDraftLine
            {
                Id = 8,
                Title = "draft",
                TemporaryId = temporaryId,
            }
        );
        session.TryReconcile(submitted, authoritative, out var error).ShouldBeTrue();

        error.ShouldBeNull();
        // Acknowledgement clears the undo retention.
        history.CanUndo.ShouldBeFalse();
        await tree.FlushAsync();

        // The temporary identity correlated to the permanent key in the live
        // model, and the current row callback resolves that same row: no
        // duplicate, no detached write.
        var assigned = board.Drafts.Single(item => item.TemporaryId == temporaryId);
        assigned.Id.ShouldBe(8);
        board.Drafts.Count.ShouldBe(2);

        var current = titles[^1];
        await tree.DispatchAsync(() => current.ValueChanged.InvokeAsync("draft v2"));
        await tree.FlushAsync();

        shown.ShouldBe("draft v2");
        board.Drafts.Single(item => item.TemporaryId == temporaryId).Title.ShouldBe("draft v2");
        board.Drafts.Select(static item => item.Id).ShouldBe([7, 8], ignoreOrder: true);
        var linesField = lines.ShouldNotBeNull();
        linesField.TryGetCollectionState(out var view).ShouldBeTrue();
        view.ShouldNotBeNull().GetItemState(8).IsEdited.ShouldBeTrue();
        session.HasChanges.ShouldBeTrue();
        // The callback edit is a new undoable transition on the clean baseline.
        history.CanUndo.ShouldBeTrue();
    }

    [Test]
    public async Task DetachedRowCallbacksNeverUpdateRemovedModel()
    {
        var live = OrderBaseline();
        var session = live.CreateEditSession();
        ISparseChildEditContext<OrderLine, SparseConflict>? rowContext = null;
        var child = ChildWithContent(
            (ISparseEditSessionWithChildren<OrderDto, SparseConflict>)session,
            OrderDto.SparsePath.Lines.Key("a"),
            context =>
                builder =>
                {
                    rowContext = context;
                    builder.AddContent(0, "row-a");
                }
        );
        using var tree = await Headless.Tree.CreateAsync(ScopeContent(session, child));

        var detached = rowContext.ShouldNotBeNull();
        ((OrderLine.EditSession)detached.Session).Observable.Quantity = 4;
        session.Observable.Lines.RemoveAt(0);
        await tree.FlushAsync();

        // The draft survives deletion in detached holding: the removed model
        // is never touched by row edits.
        detached.HasChanges.ShouldBeTrue();
        live.Lines.Select(static line => line.Sku).ShouldBe(["b"]);
        session.Observable.Lines.Select(static line => line.Sku).ShouldBe(["b"]);
        detached.TryAcceptChanges(out var acceptConflicts).ShouldBeFalse();
        acceptConflicts.ShouldBeNull();

        // Recreating the row under the same key lets the held draft land on
        // the current item: callbacks resolve the live row again.
        session.Observable.Lines.AddModel(
            new OrderLine
            {
                Sku = "a",
                Quantity = 1,
                Price = 10m,
            }
        );
        detached.TryAcceptChanges(out acceptConflicts).ShouldBeTrue();

        acceptConflicts.ShouldBeNull();
        await tree.FlushAsync();
        live.Lines.Single(line => line.Sku == "a").Quantity.ShouldBe(4);
        session
            .CreateChangeSet()
            .EnumerateChangedPaths()
            .Select(static path => path.ToString())
            .ShouldContain("Lines[\"a\"].Quantity");
        detached.HasChanges.ShouldBeFalse();
    }

    [Test]
    public async Task PreflightConflictVsLateFailureKeepsRenderedDraftsConsistent()
    {
        var live = OrderBaseline();
        var session = live.CreateEditSession();
        var history = session.EnableHistory();
        var shownCustomer = string.Empty;
        var shownNumber = string.Empty;
        ISparseChildEditContext<OrderCustomer, SparseConflict>? customerContext = null;

        var child = ChildWithContent(
            (ISparseEditSessionWithChildren<OrderDto, SparseConflict>)session,
            OrderDto.SparsePath.Customer,
            context =>
                builder =>
                {
                    customerContext = context;
                    builder.AddContent(
                        0,
                        Headless.Field(
                            OrderCustomer.SparsePath.Name,
                            field => shownCustomer = field.Value
                        )
                    );
                }
        );
        using var tree = await Headless.Tree.CreateAsync(
            ScopeContent(
                session,
                Headless.Edit(
                    session,
                    static _ => { },
                    edit =>
                        builder =>
                        {
                            builder.AddContent(
                                0,
                                Headless.Field(edit.Path.Number, field => shownNumber = field.Value)
                            );
                            builder.AddContent(1, child);
                        }
                )
            )
        );

        shownCustomer.ShouldBe("Ada");
        shownNumber.ShouldBe("ORD-1");
        var customer = customerContext.ShouldNotBeNull();
        ((OrderCustomer.EditSession)customer.Session).Observable.Name = "draft";
        session.Observable.Customer!.Name = "parent";
        await tree.FlushAsync();

        // Preflight reports the overlap as typed conflicts without throwing
        // and without mutating anything: the rendered draft stays visible.
        session.TryPreflightChildSessions(out var conflicts).ShouldBeFalse();

        var typed = conflicts.ShouldNotBeNull();
        typed.Select(static conflict => conflict.PathText).ShouldContain("Customer.Name");
        customer.HasChanges.ShouldBeTrue();
        ((OrderCustomer.EditSession)customer.Session).Observable.Name.ShouldBe("draft");
        await tree.FlushAsync();
        shownCustomer.ShouldBe("draft");

        // Undoing the parent edit clears the conflict; a late notification
        // failure during acceptance then throws and rolls the single commit
        // back, still without losing the draft or the rendered values.
        history.Undo();
        live.Customer.Name.ShouldBe("Ada");
        var armed = true;
        session.PropertyChanged += (_, _) =>
        {
            if (armed)
            {
                armed = false;
                throw new InvalidOperationException("Notification handler failed.");
            }
        };

        var failure = Should.Throw<InvalidOperationException>(() =>
        {
            session.TryAcceptChildSessions(out _);
        });

        failure.Message.ShouldBe("Notification handler failed.");
        await tree.FlushAsync();

        // The single commit rolled back atomically and the component rebound
        // to a fresh child scope: the UI truthfully shows the restored live
        // value, while the superseded scope holds its draft aside instead of
        // dropping it or presenting it as current.
        var rebound = customerContext.ShouldNotBeNull();
        ReferenceEquals(customer, rebound).ShouldBeFalse();
        customer.HasChanges.ShouldBeTrue();
        ((OrderCustomer.EditSession)customer.Session).Observable.Name.ShouldBe("draft");
        rebound.HasChanges.ShouldBeFalse();
        ((OrderCustomer.EditSession)rebound.Session).Observable.Name.ShouldBe("Ada");
        live.Customer.Name.ShouldBe("Ada");
        shownCustomer.ShouldBe("Ada");
        session.HasPendingChildSessions.ShouldBeTrue();

        // Recovery retypes through the current scope after discarding the
        // held-aside stale draft, then accepts: the rendered value and the
        // model converge on the recovered draft.
        ((OrderCustomer.EditSession)rebound.Session)
            .Observable
            .Name = "draft";
        customer.DiscardChanges();
        customer.HasChanges.ShouldBeFalse();
        await tree.FlushAsync();
        shownCustomer.ShouldBe("draft");

        armed = false;
        session.TryAcceptChildSessions(out conflicts).ShouldBeTrue();
        conflicts.ShouldBeNull();
        await tree.FlushAsync();
        shownCustomer.ShouldBe("draft");
        live.Customer.Name.ShouldBe("draft");

        armed = false;
        session.TryAcceptChildSessions(out conflicts).ShouldBeTrue();
        conflicts.ShouldBeNull();
        await tree.FlushAsync();
        shownCustomer.ShouldBe("draft");
        live.Customer.Name.ShouldBe("draft");
    }

    [Test]
    public async Task PostSubmitEditsSurviveReconcileInRenderedFields()
    {
        var live = OrderBaseline();
        var session = live.CreateEditSession();
        var history = session.EnableHistory();
        var shownNumber = string.Empty;
        var shownQuantity = 0;
        SparseFieldContext<OrderDto, string>? numberField = null;
        SparseFieldContext<OrderDto, int>? quantityField = null;

        using var tree = await Headless.Tree.CreateAsync(
            Headless.Edit(
                session,
                static _ => { },
                edit =>
                    builder =>
                    {
                        builder.AddContent(
                            0,
                            Headless.Field(
                                edit.Path.Number,
                                field =>
                                {
                                    numberField = field;
                                    shownNumber = field.Value;
                                }
                            )
                        );
                        builder.AddContent(
                            1,
                            Headless.Field(
                                edit.Path.Lines.Key("a").Quantity,
                                field =>
                                {
                                    quantityField = field;
                                    shownQuantity = field.Value;
                                }
                            )
                        );
                    }
            )
        );

        shownNumber.ShouldBe("ORD-1");
        shownQuantity.ShouldBe(1);

        session.Observable.Number = "ORD-2";
        session.Observable.Lines.Single(static line => line.Sku == "a").Quantity = 5;
        var submitted = session.CreateChangeSet();

        // Overlapping edits continue while the save is in flight.
        session.Observable.Number = "ORD-3";
        session.Observable.Lines.Single(static line => line.Sku == "a").Quantity = 6;

        submitted
            .TryApplyTo(OrderBaseline(), out var persisted, out var applyConflicts)
            .ShouldBeTrue();
        applyConflicts.ShouldBeNull();
        session.TryReconcile(submitted, persisted!, out var error).ShouldBeTrue();

        error.ShouldBeNull();
        await tree.FlushAsync();

        // The rendered fields show the merged state: submitted values
        // adopted, post-submit edits still pending and marked changed.
        shownNumber.ShouldBe("ORD-3");
        shownQuantity.ShouldBe(6);
        live.Number.ShouldBe("ORD-3");
        live.Lines.Single(static line => line.Sku == "a").Quantity.ShouldBe(6);
        numberField.ShouldNotBeNull().IsChanged.ShouldBeTrue();
        quantityField.ShouldNotBeNull().IsChanged.ShouldBeTrue();
        var pending = session.CreateChangeSet();
        pending.Number.After.Value.ShouldBe("ORD-3");
        history.CanUndo.ShouldBeFalse();
        session.HasChanges.ShouldBeTrue();
    }
}
