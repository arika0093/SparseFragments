using SparseTypes.Generated;

namespace SparseTypes.Blazor;

/// <summary>Typed capability bridge to generated session members.</summary>
/// <remarks>Hosts probe small framework-neutral capability interfaces instead of reflecting over session members, so missing capabilities fail through explicit typed contracts.</remarks>
internal static class SparseSessionInterop
{
    /// <summary>Enables the session-owned history used by a scope-owned child session.</summary>
    internal static void EnableHistory(object session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session is not ISparseEditSessionWithHistory owner)
        {
            throw new InvalidOperationException(
                $"The session type '{session.GetType()}' does not provide EnableHistory."
            );
        }

        owner.EnableHistory(null);
    }

    /// <summary>Releases session-owned history enabled for a scope-owned child session.</summary>
    internal static void DisableHistory(object session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session is not ISparseEditSessionWithHistory owner)
        {
            throw new InvalidOperationException(
                $"The session type '{session.GetType()}' does not provide DisableHistory."
            );
        }

        owner.DisableHistory();
    }

    /// <summary>Reads the session-owned history, or null when absent or not enabled.</summary>
    internal static ISparseEditHistory? TryGetHistory(object session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session is not ISparseEditSessionWithHistory owner)
        {
            return null;
        }

        try
        {
            return owner.History;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Subscribes to the session availability event, or null when the session has none.</summary>
    /// <param name="session">The edit session.</param>
    /// <param name="handler">The availability callback.</param>
    /// <returns>An unsubscriber, or null when the session exposes no such event.</returns>
    internal static Action? SubscribeHistoryChanged(object session, Action handler)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(handler);
        if (session is not ISparseEditSessionWithHistory owner)
        {
            return null;
        }

        owner.HistoryChanged += handler;
        return () => owner.HistoryChanged -= handler;
    }

    /// <summary>Queries the live semantic field state and preserves query failures for the UI.</summary>
    internal static bool TryFieldState<TModel, TValue>(
        ISparseEditSession<TModel> session,
        SparsePath<TModel, TValue> path,
        out SparseFieldState<TValue> state,
        out string? error
    )
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session is not ISparseEditSessionWithFieldState<TModel> provider)
        {
            state = default;
            error = $"The session type '{session.GetType()}' does not provide FieldState.";
            return false;
        }

        try
        {
            state = provider.FieldState(path);
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            state = default;
            error = exception.Message;
            return false;
        }
    }

    /// <summary>Reports whether a session captures reusable state-query snapshots.</summary>
    internal static bool HasStateSnapshot(object session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session is ISparseEditSessionWithStateSnapshot;
    }

    /// <summary>Captures one reusable state-query snapshot and preserves failures for the UI.</summary>
    /// <remarks>
    /// Hosts probe the snapshot capability instead of reflecting over session
    /// members, so sessions without the surface fail through an explicit typed
    /// contract and callers keep the live per-field query path.
    /// </remarks>
    internal static bool TryCaptureStateSnapshot(
        object session,
        out SparseChangeSetQueryView? snapshot,
        out string? error
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session is not ISparseEditSessionWithStateSnapshot provider)
        {
            snapshot = null;
            error = $"The session type '{session.GetType()}' does not provide state snapshots.";
            return false;
        }

        try
        {
            snapshot = provider.CaptureStateSnapshot();
            error = null;
            return snapshot is not null;
        }
        catch (Exception exception)
        {
            snapshot = null;
            error = exception.Message;
            return false;
        }
    }

    /// <summary>Queries the live collection state for a root collection path.</summary>
    /// <returns>True with the view on success; false with a null view when unavailable.</returns>
    internal static bool TryCollectionState(
        object session,
        SparsePath path,
        out SparseCollectionState? state
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session is not ISparseEditSessionWithCollectionState provider)
        {
            state = null;
            return false;
        }

        try
        {
            state = provider.CollectionState(path);
            return true;
        }
        catch (ArgumentException)
        {
            // Not a root keyed or dictionary collection path; the caller
            // reports absence.
            state = null;
            return false;
        }
    }

    internal static SparseCollectionEditor<TElement> CreateCollectionEditor<TElement>(
        object session,
        SparsePath path
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        if (
            session is not ISparseEditSessionWithDescriptors descriptorSession
            || descriptorSession.Descriptors is not IDescriptorSet descriptors
        )
        {
            throw new InvalidOperationException(
                $"The session type '{session.GetType()}' does not expose generated descriptors."
            );
        }

        if (session is not ISparseEditSessionWithBatchEdit batch)
        {
            throw new InvalidOperationException(
                $"The session type '{session.GetType()}' does not provide BatchEdit(Action)."
            );
        }

        return descriptors.EditCollection<TElement>(path, mutation => batch.BatchEdit(mutation));
    }

    internal static bool TryGetCollectionRowIdentity(
        object session,
        SparsePath path,
        int index,
        out SparsePathSegment identity,
        out object? key,
        out Guid? temporaryKey,
        out bool isKeyed,
        out string? error
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        identity = default;
        key = null;
        temporaryKey = null;
        isKeyed = false;

        if (
            session is not ISparseEditSessionWithDescriptors descriptorSession
            || descriptorSession.Descriptors is not IDescriptorSet descriptors
        )
        {
            error =
                $"The session type '{session.GetType()}' does not expose generated descriptors.";
            return false;
        }

        var array = descriptors.Find(path)?.Array;
        if (array is null)
        {
            error = $"The path '{path}' does not resolve to a generated sequence descriptor.";
            return false;
        }

        if ((uint)index >= (uint)array.Count)
        {
            error = $"Collection row {index} is no longer present.";
            return false;
        }

        isKeyed = array.IsKeyed;
        if (!isKeyed)
        {
            identity = SparsePathSegment.At(index);
            error = null;
            return true;
        }

        var itemDescriptors = array.GetItemDescriptors(index);
        if (itemDescriptors is not null)
        {
            foreach (var descriptor in itemDescriptors.Members)
            {
                if (
                    !descriptor.Attributes.Any(attribute =>
                        attribute is SparseTemporaryKeyAttribute
                    )
                )
                {
                    continue;
                }

                var value = descriptor.GetModelValue();
                if (value is Guid temporaryGuid && temporaryGuid != Guid.Empty)
                {
                    temporaryKey = temporaryGuid;
                }

                break;
            }
        }

        key = array.GetItemKey(index);
        if (key is not null && !array.IsUnassignedKey(key))
        {
            var keyType = array.KeyType;
            if (keyType is null)
            {
                error = $"The keyed collection at '{path}' does not expose its key type.";
                return false;
            }

            identity = SparsePathSegment.KeyOf(key, keyType);
            error = null;
            return true;
        }

        if (temporaryKey is { } temporary)
        {
            identity = SparsePathSegment.TemporaryKeyOf(temporary);
            error = null;
            return true;
        }

        error =
            $"The keyed collection row at index {index} has neither an assigned key nor a declared temporary identity.";
        return false;
    }
}
