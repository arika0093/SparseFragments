using SparseTypes;

namespace SparseTypes.Playground.Pages.Case6;

public sealed class CatalogChildSessionHandle<TSession> : IDisposable
    where TSession : class
{
    internal delegate bool TryAcceptChangesDelegate(out IReadOnlyList<SparseConflict>? conflicts);

    private readonly Func<bool> _hasChanges;
    private readonly TryAcceptChangesDelegate _tryAccept;
    private readonly Action _discard;
    private readonly Action _dispose;

    internal CatalogChildSessionHandle(
        TSession session,
        Func<bool> hasChanges,
        TryAcceptChangesDelegate tryAccept,
        Action discard,
        Action dispose
    )
    {
        Session = session;
        _hasChanges = hasChanges;
        _tryAccept = tryAccept;
        _discard = discard;
        _dispose = dispose;
    }

    public TSession Session { get; }
    public bool HasChanges => _hasChanges();

    public bool TryAcceptChanges(out IReadOnlyList<SparseConflict>? conflicts) =>
        _tryAccept(out conflicts);

    public void DiscardChanges() => _discard();

    public void Dispose() => _dispose();
}

public static class CatalogChildSessionHandle
{
    public static CatalogChildSessionHandle<TSession> From<TModel, TPaths, TSession>(
        ISparseEditChildSession<TModel, TPaths, SparseConflict, TSession> child
    )
        where TModel : class
        where TPaths : ISparseTypedPath<TModel, TModel>
        where TSession : class, ISparseEditSessionWithPaths<TModel, TPaths>
    {
        return new CatalogChildSessionHandle<TSession>(
            child.Session,
            () => child.HasChanges,
            child.TryAcceptChanges,
            child.DiscardChanges,
            child.Dispose
        );
    }
}
