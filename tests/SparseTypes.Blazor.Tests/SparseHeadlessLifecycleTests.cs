using System.ComponentModel;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace SparseTypes.Blazor.Tests;

// Headless lifecycle for session-owned history without extra calls and across
// remounts.
public sealed class SparseHeadlessLifecycleTests
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

    private static object SessionCore(object session)
    {
        // Generated sessions forward PropertyChanged to a composed core.
        for (var type = session.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field?.GetValue(session) is { } core)
            {
                return core;
            }
        }

        return session;
    }

    private static int ListenerCount(object session)
    {
        var target = SessionCore(session);
        for (var type = target.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField(
                "PropertyChanged",
                BindingFlags.NonPublic | BindingFlags.Instance
            );
            if (field is not null)
            {
                return field.GetValue(target) is PropertyChangedEventHandler handler
                    ? handler.GetInvocationList().Length
                    : 0;
            }
        }

        return -1;
    }

    private static void CountInside(
        RenderTreeBuilder builder,
        Action<Headless.Counter> capture,
        RenderFragment child
    )
    {
        builder.OpenComponent<Headless.Counter>(0);
        builder.AddAttribute(1, nameof(Headless.Counter.ChildContent), child);
        builder.AddComponentReferenceCapture(2, component => capture((Headless.Counter)component));
        builder.CloseComponent();
    }

    [Test]
    public async Task HistoryUpdatesWithoutExtraCallsAndSurvivesRemount()
    {
        var live = Order();
        var session = live.CreateEditSession();
        var history = session.EnableHistory();
        var initialHistory = session.History;
        ReferenceEquals(history, initialHistory).ShouldBeTrue();

        var canUndo = false;
        var canRedo = false;
        var undoCount = -1;
        Action undo = () =>
            throw new InvalidOperationException("The edit context was not captured.");
        SparseFieldContext<OrderDto, string>? field = null;
        Headless.Counter? counter = null;
        var fragment = Headless.Edit(
            session,
            edit =>
            {
                canUndo = edit.CanUndo;
                canRedo = edit.CanRedo;
                undoCount = edit.History!.UndoCount;
                undo = edit.Undo;
            },
            edit =>
                builder =>
                    CountInside(
                        builder,
                        captured => counter = captured,
                        Headless.Field(edit.Path.Number, captured => field = captured)
                    )
        );

        using var tree = await Headless.Tree.CreateAsync(fragment);
        var baseline = counter!.Renders;
        canUndo.ShouldBeFalse();
        undoCount.ShouldBe(0);

        var fieldValue = field.ShouldNotBeNull();
        await tree.DispatchAsync(() => fieldValue.ValueChanged.InvokeAsync("ORD-2"));
        // The session and history notifications refresh on their own: the
        // counter moves without the test re-showing anything.
        await tree.FlushAsync();
        counter.Renders.ShouldBeGreaterThan(baseline);
        canUndo.ShouldBeTrue();
        undoCount.ShouldBe(1);
        live.Number.ShouldBe("ORD-2");

        await tree.DispatchAsync(() => undo());
        await tree.FlushAsync();
        live.Number.ShouldBe("ORD-1");
        canUndo.ShouldBeFalse();
        canRedo.ShouldBeTrue();

        // Remounting with the same session keeps the session-owned history:
        // counts and availability survive, and nothing was disposed.
        tree.Dispose();
        session.History.ShouldNotBeNull();
        ReferenceEquals(session.History, initialHistory).ShouldBeTrue();
        session.History.UndoCount.ShouldBe(0);
        session.History.RedoCount.ShouldBe(1);

        canUndo = true;
        using var remounted = await Headless.Tree.CreateAsync(
            Headless.Edit(
                session,
                edit =>
                {
                    canUndo = edit.CanUndo;
                    canRedo = edit.CanRedo;
                }
            )
        );
        canUndo.ShouldBeFalse();
        canRedo.ShouldBeTrue();
        session.History.Redo();
        live.Number.ShouldBe("ORD-2");

        session.DisableHistory();
    }

    [Test]
    public async Task RenderingFieldAndCollectionStateKeepsChildDraftPending()
    {
        var live = Order();
        var session = live.CreateEditSession();
        var child = session.OpenChildSession(OrderDto.SparsePath.Customer);
        child.Session.Observable.Name = "Grace";
        SparseFieldContext<OrderDto, List<OrderLine>>? field = null;

        using var tree = await Headless.Tree.CreateAsync(
            Headless.Edit(
                session,
                _ => { },
                edit =>
                    builder =>
                        builder.AddContent(
                            0,
                            Headless.Field(edit.Path.Lines, captured => field = captured)
                        )
            )
        );

        field.ShouldNotBeNull();
        field!.TryGetCollectionState(out var collection).ShouldBeTrue();
        collection!.Value.GetItemState("a").IsEmpty.ShouldBeTrue();
        child.HasChanges.ShouldBeTrue();
        live.Customer.Name.ShouldBe("Ada");
    }

    [Test]
    public async Task HistoryAvailabilityRewiresWithoutLeaking()
    {
        var live = Order();
        var session = live.CreateEditSession();
        var canUndo = true;
        var hasHistory = true;
        Action undo = () =>
            throw new InvalidOperationException("The edit context was not captured.");
        SparseFieldContext<OrderDto, string>? field = null;

        using var tree = await Headless.Tree.CreateAsync(
            Headless.Edit(
                session,
                edit =>
                {
                    canUndo = edit.CanUndo;
                    hasHistory = edit.History is not null;
                    undo = edit.Undo;
                },
                edit => Headless.Field(edit.Path.Number, captured => field = captured)
            )
        );

        canUndo.ShouldBeFalse();
        hasHistory.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => undo());

        session.EnableHistory();
        await tree.FlushAsync();
        hasHistory.ShouldBeTrue();
        canUndo.ShouldBeFalse();

        var fieldValue = field.ShouldNotBeNull();
        await tree.DispatchAsync(() => fieldValue.ValueChanged.InvokeAsync("ORD-2"));
        await tree.FlushAsync();
        canUndo.ShouldBeTrue();

        session.DisableHistory();
        await tree.FlushAsync();
        hasHistory.ShouldBeFalse();
        canUndo.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => undo());
        live.Number.ShouldBe("ORD-2");
    }

    [Test]
    public async Task SessionSwapRebuildsContextWithoutLeaksOrStaleWrites()
    {
        var live1 = Order();
        var session1 = live1.CreateEditSession();
        var live2 = Order();
        live2.Number = "ORD-2";
        var session2 = live2.CreateEditSession();
        ListenerCount(session1).ShouldBe(0);

        ISparseEditScope<OrderDto>? scope = null;
        EditContext? editContext = null;
        SparseFieldContext<OrderDto, string>? field = null;
        var shown = string.Empty;
        RenderFragment show1 = Headless.Edit(
            session1,
            edit =>
            {
                scope = edit;
                editContext = edit.EditContext;
            },
            edit =>
                Headless.Field(
                    edit.Path.Number,
                    captured =>
                    {
                        field = captured;
                        shown = captured.Value;
                    }
                )
        );

        using var tree = await Headless.Tree.CreateAsync(show1);
        ListenerCount(session1).ShouldBeGreaterThan(0);
        shown.ShouldBe("ORD-1");
        var firstField = field.ShouldNotBeNull();
        var firstContext = editContext.ShouldNotBeNull();

        await tree.ShowAsync(
            Headless.Edit(
                session2,
                edit =>
                {
                    scope = edit;
                    editContext = edit.EditContext;
                },
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

        // The previous session no longer refreshes this tree.
        ListenerCount(session1).ShouldBe(0);
        ListenerCount(session2).ShouldBeGreaterThan(0);
        ReferenceEquals(editContext, firstContext).ShouldBeFalse();
        ReferenceEquals(editContext!.Model, live2).ShouldBeTrue();
        ReferenceEquals(field, firstField).ShouldBeFalse();
        shown.ShouldBe("ORD-2");

        // The previous binding still targets the previous session only:
        // nothing crosses into the replacement model.
        firstField.TrySet("STALE", out _).ShouldBeTrue();
        await tree.FlushAsync();
        live1.Number.ShouldBe("STALE");
        live2.Number.ShouldBe("ORD-2");
        shown.ShouldBe("ORD-2");

        var secondField = field.ShouldNotBeNull();
        await tree.DispatchAsync(() => secondField.ValueChanged.InvokeAsync("ORD-3"));
        await tree.FlushAsync();
        live2.Number.ShouldBe("ORD-3");
        live1.Number.ShouldBe("STALE");
        shown.ShouldBe("ORD-3");

        await tree.ShowAsync(static builder => { });
        ListenerCount(session2).ShouldBe(0);
        ReferenceEquals(scope!.Session, session2).ShouldBeTrue();
    }

    [Test]
    public async Task BatchEditRefreshesOnce()
    {
        var session = Order().CreateEditSession();
        Headless.Counter? counter = null;
        // The counter sits inside the edit subtree, so it observes exactly the
        // renders the session notification produces.
        using var tree = await Headless.Tree.CreateAsync(
            Headless.Edit(
                session,
                static _ => { },
                edit =>
                    builder =>
                        CountInside(
                            builder,
                            captured => counter = captured,
                            Headless.Field(edit.Path.Number, static _ => { })
                        )
            )
        );

        var baseline = counter!.Renders;
        session.BatchEdit(() =>
        {
            session.Observable.Number = "ORD-2";
            session.Observable.Customer!.Name = "Grace";
            session.Observable.Tags.Add("heavy");
        });
        await tree.FlushAsync();
        counter.Renders.ShouldBe(baseline + 1);
    }

    [Test]
    public async Task DisposeStopsRefresh()
    {
        var session = Order().CreateEditSession();
        Headless.Counter? counter = null;
        var tree = await Headless.Tree.CreateAsync(builder =>
            CountInside(
                builder,
                captured => counter = captured,
                Headless.Edit(session, static _ => { })
            )
        );

        var baseline = counter!.Renders;
        await tree.ShowAsync(static builder => { });
        ListenerCount(session).ShouldBe(0);

        session.Observable.Number = "ORD-2";
        await tree.FlushAsync();
        counter.Renders.ShouldBe(baseline);
        tree.Dispose();
    }

    [Test]
    public async Task RowReplacementRefreshesBindingWithoutStaleWrites()
    {
        var live = Order();
        var session = live.CreateEditSession();
        var oldRow = live.Lines.Single(line => line.Sku == "b");
        SparseFieldContext<OrderDto, decimal>? field = null;
        var shown = 0m;

        using var tree = await Headless.Tree.CreateAsync(
            Headless.Edit(
                session,
                static _ => { },
                edit =>
                    Headless.Field(
                        edit.Path.Lines.Key("b").Price,
                        captured =>
                        {
                            field = captured;
                            if (captured.TryGetValue(out var value, out _))
                            {
                                shown = value;
                            }
                        }
                    )
            )
        );

        var price = field.ShouldNotBeNull();
        shown.ShouldBe(20m);

        session
            .EditCollection<OrderLine, string>(OrderDto.SparsePath.Lines)
            .TryReplaceByKey(
                "b",
                new OrderLine
                {
                    Sku = "b",
                    Quantity = 5,
                    Price = 50m,
                },
                out _
            )
            .ShouldBeTrue();
        await tree.FlushAsync();

        // The binding follows the key to the replacement instance.
        shown.ShouldBe(50m);
        price.IsStale.ShouldBeFalse();
        oldRow.Quantity.ShouldBe(2);
        oldRow.Price.ShouldBe(20m);

        await tree.DispatchAsync(() => price.ValueChanged.InvokeAsync(77m));
        await tree.FlushAsync();
        live.Lines.Single(line => line.Sku == "b").Price.ShouldBe(77m);
        oldRow.Price.ShouldBe(20m);
    }

    [Test]
    public async Task ReorderFollowsKeysWhileIndexesStayPositional()
    {
        var live = Order();
        var session = live.CreateEditSession();
        SparseFieldContext<OrderDto, int>? keyed = null;
        SparseFieldContext<OrderDto, string>? indexed = null;
        var keyedQuantity = 0;
        var indexedSku = string.Empty;

        using var tree = await Headless.Tree.CreateAsync(
            Headless.Edit(
                session,
                static _ => { },
                edit =>
                    Headless.Field(
                        edit.Path.Lines,
                        static _ => { },
                        lines =>
                            Headless.Field(
                                lines.Key("a", (OrderLine row) => row.Quantity),
                                captured =>
                                {
                                    keyed = captured;
                                    keyedQuantity = captured.Value;
                                }
                            )
                    )
            )
        );

        keyed.ShouldNotBeNull();
        keyedQuantity.ShouldBe(1);

        using var indexedTree = await Headless.Tree.CreateAsync(
            Headless.Edit(
                session,
                static _ => { },
                edit =>
                    Headless.Field(
                        edit.Path.Lines,
                        static _ => { },
                        lines =>
                            Headless.Field(
                                lines.At(0, (OrderLine row) => row.Sku),
                                captured =>
                                {
                                    indexed = captured;
                                    indexedSku = captured.Value;
                                }
                            )
                    )
            )
        );

        indexed.ShouldNotBeNull();
        indexedSku.ShouldBe("a");

        session.Observable.Lines.Move(0, 1);
        await tree.FlushAsync();
        await indexedTree.FlushAsync();

        keyedQuantity.ShouldBe(1);
        keyed!.Value.ShouldBe(1);
        indexedSku.ShouldBe("b");
        live.Lines[0].Sku.ShouldBe("b");
    }

    [Test]
    public async Task TemporaryKeyAndCollectionStateFlow()
    {
        var board = new BlazorDraftBoard { Name = "Board" };
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

        SparseFieldContext<BlazorDraftBoard, List<BlazorDraftLine>>? drafts = null;
        SparseFieldContext<BlazorDraftBoard, string>? title = null;
        var shownTitle = string.Empty;
        var expressionMissing = false;
        var collectionHit = false;
        var itemAdded = false;

        using var tree = await Headless.Tree.CreateAsync(
            Headless.Edit(
                session,
                static _ => { },
                edit =>
                    Headless.Field(
                        edit.Path.Drafts,
                        captured =>
                        {
                            drafts = captured;
                            if (captured.TryGetCollectionState(out var state) && state is not null)
                            {
                                collectionHit = true;
                                itemAdded = state.Value.GetTemporaryState(temporaryId).IsAdded;
                            }
                        },
                        captured =>
                            Headless.Field(
                                BlazorDraftBoard.SparsePath.Drafts.TemporaryKey(temporaryId).Title,
                                nested =>
                                {
                                    title = nested;
                                    shownTitle = nested.Value;
                                    expressionMissing = nested.ValueExpression is null;
                                }
                            )
                    )
            )
        );

        var titleField = title.ShouldNotBeNull();
        shownTitle.ShouldBe("draft");
        expressionMissing.ShouldBeTrue();
        collectionHit.ShouldBeTrue();
        itemAdded.ShouldBeTrue();

        // The context navigation builds the same segments as the generated
        // builder.
        var draftsField = drafts.ShouldNotBeNull();
        draftsField
            .TemporaryKey(temporaryId, (BlazorDraftLine row) => row.Title)
            .ShouldBe(BlazorDraftBoard.SparsePath.Drafts.TemporaryKey(temporaryId).Title);

        // Collection-root fields report the default semantic state (member
        // transitions cannot be boxed as the collection value).
        draftsField.State.IsChanged.ShouldBeFalse();

        await tree.DispatchAsync(() => titleField.ValueChanged.InvokeAsync("draft v2"));
        await tree.FlushAsync();
        board.Drafts.Single(item => item.TemporaryId == temporaryId).Title.ShouldBe("draft v2");
        shownTitle.ShouldBe("draft v2");

        // Non-collection fields report no collection state.
        SparseFieldContext<OrderDto, string>? scalar = null;
        var scalarCollectionHit = true;
        var orderSession = Order().CreateEditSession();
        using var scalarTree = await Headless.Tree.CreateAsync(
            Headless.Edit(
                orderSession,
                static _ => { },
                edit =>
                    Headless.Field(
                        edit.Path.Number,
                        captured =>
                        {
                            scalar = captured;
                            scalarCollectionHit = captured.TryGetCollectionState(out _);
                        }
                    )
            )
        );

        scalar.ShouldNotBeNull();
        scalarCollectionHit.ShouldBeFalse();
    }

    [Test]
    public async Task KeyedPathsRejectInputBaseButEditThroughVendorContract()
    {
        var live = Order();
        var session = live.CreateEditSession();
        SparseFieldContext<OrderDto, int>? field = null;
        Headless.VendorField<int>? vendor = null;

        using var tree = await Headless.Tree.CreateAsync(
            Headless.Edit(
                session,
                static _ => { },
                edit =>
                    Headless.Field(
                        edit.Path.Lines.Key("b").Quantity,
                        captured => field = captured,
                        captured =>
                            builder =>
                            {
                                builder.OpenComponent<Headless.VendorField<int>>(0);
                                builder.AddAttribute(1, "Value", captured.Value);
                                builder.AddAttribute(2, "ValueChanged", captured.ValueChanged);
                                builder.AddComponentReferenceCapture(
                                    3,
                                    component => vendor = (Headless.VendorField<int>)component
                                );
                                builder.CloseComponent();
                            }
                    )
            )
        );

        var quantity = field.ShouldNotBeNull();
        vendor.ShouldNotBeNull();
        // InputBase rejects a null expression, so the supported contract is
        // value/callback plus explicit validation state.
        quantity.ValueExpression.ShouldBeNull();
        quantity.Value.ShouldBe(2);

        await tree.DispatchAsync(() => vendor!.ValueChanged.InvokeAsync(9));
        await tree.FlushAsync();
        live.Lines.Single(line => line.Sku == "b").Quantity.ShouldBe(9);
        quantity.IsChanged.ShouldBeTrue();
        quantity.IsModified.ShouldBeTrue();

        var failure = await tree.TryShowAsync(
            Headless.Edit(
                session,
                static _ => { },
                edit =>
                    Headless.Field(
                        edit.Path.Lines.Key("b").Quantity,
                        static _ => { },
                        captured =>
                            builder =>
                            {
                                builder.OpenComponent<InputNumber<int>>(0);
                                builder.AddAttribute(
                                    1,
                                    nameof(InputNumber<int>.Value),
                                    captured.Value
                                );
                                builder.AddAttribute(
                                    2,
                                    nameof(InputNumber<int>.ValueChanged),
                                    captured.ValueChanged
                                );
                                builder.AddAttribute(
                                    3,
                                    nameof(InputNumber<int>.ValueExpression),
                                    captured.ValueExpression
                                );
                                builder.CloseComponent();
                            }
                    )
            )
        );
        failure.ShouldNotBeNull();
        failure!.ToString().ShouldContain("ValueExpression");
    }

    [Test]
    public async Task FieldOutsideEditFailsWithGuidance()
    {
        using var tree = await Headless.Tree.CreateAsync(static builder => { });
        var failure = await tree.TryShowAsync(
            Headless.Field(OrderDto.SparsePath.Number, static _ => { })
        );
        failure.ShouldNotBeNull();
        failure!.ToString().ShouldContain("SparseEdit");
    }
}
