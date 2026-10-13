namespace SparseTypes.EntityFrameworkCore.Mapping;

/// <summary>
/// Ambient journal for staged temporary-key reconciliation.
/// </summary>
/// <remarks>
/// <see cref="SparseEntityMapper{TEntity, TDto}.AssignTemporaryKeys" /> opens one
/// scope per call. Every applied temporary-key mutation records its restore
/// action here, so a failure in a deep nested collection rolls back earlier
/// sibling assignments and the authoritative DTO is never left partially
/// updated. Restores are best effort: a throwing restore never masks the
/// original failure.
/// </remarks>
internal sealed class TemporaryKeyStagingScope
{
    private static readonly AsyncLocal<TemporaryKeyStagingScope?> CurrentStorage = new();

    private readonly List<Action> _restores = [];

    private bool _closed;

    /// <summary>Gets the ambient reconciliation scope, when one is open.</summary>
    internal static TemporaryKeyStagingScope? Current => CurrentStorage.Value;

    /// <summary>Opens the ambient scope for one reconciliation call.</summary>
    /// <returns>The opened scope.</returns>
    internal static TemporaryKeyStagingScope Begin()
    {
        var scope = new TemporaryKeyStagingScope();
        CurrentStorage.Value = scope;
        return scope;
    }

    /// <summary>Records how to undo one applied temporary-key mutation.</summary>
    /// <param name="restore">The restore action.</param>
    internal void Record(Action restore)
    {
        ArgumentNullException.ThrowIfNull(restore);
        _restores.Add(restore);
    }

    /// <summary>Undoes every recorded mutation, newest first.</summary>
    internal void Rollback()
    {
        for (var index = _restores.Count - 1; index >= 0; index--)
        {
            try
            {
                _restores[index]();
            }
            catch (Exception)
            {
                // Best effort: a failing restore must not mask the original failure.
            }
        }
    }

    /// <summary>Closes the ambient scope.</summary>
    internal void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        if (ReferenceEquals(CurrentStorage.Value, this))
        {
            CurrentStorage.Value = null;
        }
    }
}
