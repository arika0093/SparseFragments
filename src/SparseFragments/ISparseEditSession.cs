namespace SparseFragments;

/// <summary>The minimal typed edit-session contract used by framework integrations.</summary>
/// <typeparam name="TModel">The live editable model type.</typeparam>
public interface ISparseEditSession<out TModel>
    where TModel : class
{
    /// <summary>Gets the live model instance.</summary>
    TModel Model { get; }

    /// <summary>Gets whether the current model differs from the retained baseline.</summary>
    bool HasChanges { get; }

    /// <summary>Accepts the current model state as the new baseline.</summary>
    void AcceptChanges();
}

/// <summary>A typed edit-session contract that can accept a generated change set.</summary>
/// <typeparam name="TModel">The live editable model type.</typeparam>
/// <typeparam name="TChangeSet">The generated change-set type.</typeparam>
public interface ISparseEditSession<out TModel, in TChangeSet> : ISparseEditSession<TModel>
    where TModel : class
    where TChangeSet : class
{
    /// <summary>Advances the retained baseline by a generated change set.</summary>
    void AcceptChanges(TChangeSet changes);
}
