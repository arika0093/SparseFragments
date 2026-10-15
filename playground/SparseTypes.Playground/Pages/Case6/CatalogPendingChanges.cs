using SparseTypes;
using SparseTypes.Generated;
using SparseTypes.Playground.Models;

namespace SparseTypes.Playground.Pages.Case6;

// Root-owned (already merged) change marking for the catalog editor.
//
// Child-local draft state (child HasChanges, field IsChanged, row ItemState)
// covers edits still pending in a section draft; this view covers changes
// already merged into the root session but not yet persisted. The general
// Edited state is either of the two, in both section-drafts and AutoAccept
// modes. Captured once per root render from a single shared snapshot, so
// fields and rows never re-derive the root diff on their own.
public sealed class CatalogPendingChanges
{
    private readonly SparseChangeSetQueryView _view;

    private CatalogPendingChanges(SparseChangeSetQueryView view)
    {
        _view = view;
    }

    public static CatalogPendingChanges? TryCaptureRoot(EnterpriseCatalogItem.EditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        try
        {
            // Own changes only: pending child drafts stay covered by
            // child-local state, never double-counted here.
            var view = ((ISparseChangeSet)session.PreviewChangeSet()).CaptureQueryView();
            view.EnsureIndexed();
            return new CatalogPendingChanges(view);
        }
        catch (Exception exception)
            when (exception
                    is InvalidOperationException
                        or ArgumentException
                        or ObjectDisposedException
            )
        {
            // The live model is temporarily undiffable (for example duplicate
            // keyed keys). Fall back to draft-only marking for this render.
            return null;
        }
    }

    // Section and tab badges: any exact, ancestor, or descendant change
    // overlaps the section subtree.
    public bool IsSubtreeChanged(SparsePath sectionPath) =>
        _view.FieldState(sectionPath).HasChanges;

    // Scalar or collection fields: an exact or descendant change. A bare
    // ancestor change (a whole parent replaced at once) never paints every
    // descendant field.
    public bool IsFieldChanged(SparsePath rootFieldPath)
    {
        var state = _view.FieldState(rootFieldPath);
        return state.IsChanged || state.HasDescendantChange;
    }

    // Keyed-row state at its catalog-rooted path. Added, Removed, and Changed
    // report exactly; Order reports at the collection path instead of rows.
    public SparseFieldState<object?> RowState(SparsePath rowPath) => _view.FieldState(rowPath);

    // Removed-row placeholders directly under a catalog-rooted collection
    // path, newest first in record order. Before carries the removed element.
    public IReadOnlyList<SparseChangeRecord> RemovedRows(SparsePath collectionPath)
    {
        var removed = new List<SparseChangeRecord>();
        foreach (var record in _view.Records)
        {
            if (
                record.Kind == SparseChangeKind.Removed
                && record.Path.Parent?.Equals(collectionPath) == true
            )
            {
                removed.Add(record);
            }
        }

        return removed;
    }

    // Before/after string lists for an unkeyed scalar collection (the
    // Overview aliases diff whole-member). Empty when the member is untouched.
    public bool TryGetStringListEndpoints(
        SparsePath collectionPath,
        out IReadOnlyList<string> before,
        out IReadOnlyList<string> after
    ) => TryGetStringListEndpoints(_view, collectionPath, out before, out after);

    // Before/after string lists for an unkeyed scalar collection (the
    // Overview aliases diff whole-member). Empty when the member is untouched.
    public static bool TryGetStringListEndpoints(
        SparseChangeSetQueryView view,
        SparsePath collectionPath,
        out IReadOnlyList<string> before,
        out IReadOnlyList<string> after
    )
    {
        ArgumentNullException.ThrowIfNull(view);
        before = [];
        after = [];
        var state = view.FieldState(collectionPath);
        if (!state.IsChanged || !state.Before.IsPresent || !state.After.IsPresent)
        {
            return false;
        }

        if (
            state.Before.Value is not System.Collections.IEnumerable beforeValues
            || state.After.Value is not System.Collections.IEnumerable afterValues
        )
        {
            return false;
        }

        before = beforeValues
            .Cast<object?>()
            .Select(static value => value?.ToString() ?? string.Empty)
            .ToArray();
        after = afterValues
            .Cast<object?>()
            .Select(static value => value?.ToString() ?? string.Empty)
            .ToArray();
        return true;
    }
}
