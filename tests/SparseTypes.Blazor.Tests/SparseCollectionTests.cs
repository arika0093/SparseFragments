using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace SparseTypes.Blazor.Tests;

public sealed class SparseCollectionTests
{
    private static RenderFragment EditWithCollection<TModel, TPaths, TElement>(
        ISparseEditSessionWithPaths<TModel, TPaths> session,
        SparsePath path,
        Action<SparseCollection<TModel, TElement>> capture
    )
        where TModel : class
        where TPaths : ISparseTypedPath<TModel, TModel>
        where TElement : class =>
        builder =>
        {
            builder.OpenComponent<SparseEdit<TModel, TPaths>>(0);
            builder.AddAttribute(1, nameof(SparseEdit<TModel, TPaths>.Session), session);
            builder.AddAttribute(
                2,
                nameof(SparseEdit<TModel, TPaths>.ChildContent),
                (RenderFragment<SparseEditContext<TModel, TPaths>>)(
                    _ =>
                        inner =>
                        {
                            inner.OpenComponent<SparseCollection<TModel, TElement>>(0);
                            inner.AddAttribute(
                                1,
                                nameof(SparseCollection<TModel, TElement>.Path),
                                path
                            );
                            inner.AddAttribute(
                                2,
                                nameof(SparseCollection<TModel, TElement>.RowTemplate),
                                (RenderFragment<SparseCollectionRow<TModel, TElement>>)(
                                    row => rowBuilder => rowBuilder.AddContent(0, row.Item)
                                )
                            );
                            inner.AddComponentReferenceCapture(
                                3,
                                component => capture((SparseCollection<TModel, TElement>)component)
                            );
                            inner.CloseComponent();
                        }
                )
            );
            builder.CloseComponent();
        };

    [Test]
    public async Task CollectionRowsKeepIdentityAcrossKeyAssignmentAndSupportEdits()
    {
        var temporaryId = Guid.NewGuid();
        var board = new BlazorDraftBoard
        {
            Drafts =
            [
                new BlazorDraftLine { Id = 2, Title = "Existing" },
                new BlazorDraftLine { Id = 3, Title = "Other" },
            ],
        };
        var session = board.CreateEditSession();
        SparseCollection<BlazorDraftBoard, BlazorDraftLine>? collection = null;

        using var tree = await Headless.Tree.CreateAsync(
            EditWithCollection(
                session,
                BlazorDraftBoard.SparsePath.Drafts,
                (SparseCollection<BlazorDraftBoard, BlazorDraftLine> captured) =>
                    collection = captured
            )
        );

        var view = collection.ShouldNotBeNull();
        var existing = view.Rows[0];
        var other = view.Rows[1];
        existing.UiKey.ShouldNotBe(Guid.Empty);
        existing.Path.UntypedPath.ShouldBe(BlazorDraftBoard.SparsePath.Drafts.Key(2).UntypedPath);
        existing.ItemState.ShouldNotBeNull();
        existing.ItemState!.Value.IsEmpty.ShouldBeTrue();

        view.TryAdd(
                new BlazorDraftLine
                {
                    Id = 0,
                    TemporaryId = temporaryId,
                    Title = "Draft",
                },
                out var addDraftError
            )
            .ShouldBeTrue();
        addDraftError.ShouldBe(SparseCollectionEditError.None);
        var rowsAfterAdd = view.Rows;
        await tree.FlushAsync();
        ReferenceEquals(rowsAfterAdd, view.Rows).ShouldBeTrue();

        var draft = view.Rows[2];
        draft.UiKey.ShouldNotBe(Guid.Empty);
        draft.UiKey.ShouldNotBe(existing.UiKey);
        draft.Path.UntypedPath.ShouldBe(
            BlazorDraftBoard.SparsePath.Drafts.TemporaryKey(temporaryId).UntypedPath
        );
        draft
            .Select(item => item.Title)
            .UntypedPath.ShouldBe(
                BlazorDraftBoard.SparsePath.Drafts.TemporaryKey(temporaryId).Title.UntypedPath
            );
        draft.ItemState.ShouldNotBeNull();
        draft.ItemState!.Value.IsAdded.ShouldBeTrue();

        draft.TryMoveTo(0, out var moveDraftError).ShouldBeTrue();
        moveDraftError.ShouldBe(SparseCollectionEditError.None);
        await tree.FlushAsync();
        view.Rows[0].UiKey.ShouldBe(draft.UiKey);
        view.Rows[0]
            .Path.UntypedPath.ShouldBe(
                BlazorDraftBoard.SparsePath.Drafts.TemporaryKey(temporaryId).UntypedPath
            );
        view.Rows[0].ItemState!.Value.IsAdded.ShouldBeTrue();

        session.Observable.Drafts[0].Id = 10;
        session.Observable.Drafts[0].TemporaryId = null;
        await tree.FlushAsync();

        view.Rows[0].UiKey.ShouldBe(draft.UiKey);
        view.Rows[0].Item.Id.ShouldBe(10);
        view.Rows[0]
            .Path.UntypedPath.ShouldBe(BlazorDraftBoard.SparsePath.Drafts.Key(10).UntypedPath);
        view.Rows[0].ItemState.ShouldNotBeNull();
        view.Rows[0].ItemState!.Value.IsAdded.ShouldBeTrue();

        existing.TryMoveTo(2, out var moveError).ShouldBeTrue();
        moveError.ShouldBe(SparseCollectionEditError.None);
        await tree.FlushAsync();
        view.Rows[2].UiKey.ShouldBe(existing.UiKey);
        view.Rows[2]
            .Path.UntypedPath.ShouldBe(BlazorDraftBoard.SparsePath.Drafts.Key(2).UntypedPath);
        view.Rows[2].ItemState!.Value.IsReordered.ShouldBeTrue();

        view.TryAdd(new BlazorDraftLine { Id = 4, Title = "Added" }, out var addError)
            .ShouldBeTrue();
        addError.ShouldBe(SparseCollectionEditError.None);
        await tree.FlushAsync();
        view.Rows.Count.ShouldBe(4);

        view.Rows[3].TryRemove(out var removeError).ShouldBeTrue();
        removeError.ShouldBe(SparseCollectionEditError.None);
        await tree.FlushAsync();
        view.Rows.Count.ShouldBe(3);
        board
            .Drafts.Select(static item => item.Title)
            .ShouldBe(new[] { "Draft", "Other", "Existing" });
    }

    [Test]
    public async Task ReconcileAndApplyKeepsRowIdentityAndActionsAcrossModelReplacement()
    {
        var temporaryId = Guid.NewGuid();
        var board = new BlazorDraftBoard
        {
            Drafts = [new BlazorDraftLine { Id = 7, Title = "Existing" }],
        };
        var session = board.CreateEditSession();
        SparseCollection<BlazorDraftBoard, BlazorDraftLine>? collection = null;

        using var tree = await Headless.Tree.CreateAsync(
            EditWithCollection(
                session,
                BlazorDraftBoard.SparsePath.Drafts,
                (SparseCollection<BlazorDraftBoard, BlazorDraftLine> captured) =>
                    collection = captured
            )
        );

        var view = collection.ShouldNotBeNull();
        view.TryAdd(
                new BlazorDraftLine { TemporaryId = temporaryId, Title = "Pending" },
                out var addError
            )
            .ShouldBeTrue();
        addError.ShouldBe(SparseCollectionEditError.None);
        await tree.FlushAsync();

        var pendingRow = view.Rows.Single(row => row.Item.TemporaryId == temporaryId);
        var submitted = session.CreateChangeSet();
        var persisted = new BlazorDraftBoard
        {
            Drafts =
            [
                new BlazorDraftLine { Id = 7, Title = "Existing" },
                new BlazorDraftLine
                {
                    Id = 42,
                    TemporaryId = temporaryId,
                    Title = "Persisted",
                },
            ],
        };

        session.TryReconcileAndApply(submitted, persisted, out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        await tree.FlushAsync();
        view.Rows.Count.ShouldBe(2);
        view.Rows[1].Item.ShouldNotBeSameAs(pendingRow.Item);
        view.Rows[1].UiKey.ShouldBe(pendingRow.UiKey);
        view.Rows[1]
            .Path.UntypedPath.ShouldBe(BlazorDraftBoard.SparsePath.Drafts.Key(42).UntypedPath);

        pendingRow.TryMoveTo(0, out var moveError).ShouldBeTrue();
        moveError.ShouldBe(SparseCollectionEditError.None);
        await tree.FlushAsync();
        view.Rows[0].UiKey.ShouldBe(pendingRow.UiKey);

        pendingRow.TryRemove(out var removeError).ShouldBeTrue();
        removeError.ShouldBe(SparseCollectionEditError.None);
        await tree.FlushAsync();
        view.Rows.ShouldHaveSingleItem();
        board.Drafts.Select(static item => item.Id).ShouldBe([7]);
        view.RemovedItems.ShouldHaveSingleItem().Title.ShouldBe("Persisted");
    }

    [Test]
    public async Task SessionReplacementRefreshesRowsAndRejectsOldRowCallbacks()
    {
        var firstBoard = new BlazorDraftBoard
        {
            Drafts = [new BlazorDraftLine { Id = 7, Title = "First" }],
        };
        var firstSession = firstBoard.CreateEditSession();
        SparseCollection<BlazorDraftBoard, BlazorDraftLine>? collection = null;

        using var tree = await Headless.Tree.CreateAsync(
            EditWithCollection(
                firstSession,
                BlazorDraftBoard.SparsePath.Drafts,
                (SparseCollection<BlazorDraftBoard, BlazorDraftLine> captured) =>
                    collection = captured
            )
        );

        var staleRow = collection.ShouldNotBeNull().Rows[0];
        var secondBoard = new BlazorDraftBoard
        {
            Drafts = [new BlazorDraftLine { Id = 7, Title = "Second" }],
        };
        var secondSession = secondBoard.CreateEditSession();

        await tree.ShowAsync(
            EditWithCollection(
                secondSession,
                BlazorDraftBoard.SparsePath.Drafts,
                (SparseCollection<BlazorDraftBoard, BlazorDraftLine> captured) =>
                    collection = captured
            )
        );

        var view = collection.ShouldNotBeNull();
        view.Rows.ShouldHaveSingleItem().Item.ShouldBeSameAs(secondBoard.Drafts[0]);
        staleRow.TryRemove(out var staleError).ShouldBeFalse();
        staleError.ShouldBe(SparseCollectionEditError.UnknownCollection);
        secondBoard.Drafts.ShouldHaveSingleItem();

        view.Rows[0].TryRemove(out var removeError).ShouldBeTrue();
        removeError.ShouldBe(SparseCollectionEditError.None);
        await tree.FlushAsync();
        secondBoard.Drafts.ShouldBeEmpty();
    }
}
