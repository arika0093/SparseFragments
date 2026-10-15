namespace SparseTypes;

/// <summary>Trusted read-only access to a session's live model for framework integrations.</summary>
/// <remarks>Unlike <see cref="ISparseEditSession{TModel}.Model"/>, this access preserves the session's observable-change cache.</remarks>
/// <typeparam name="TModel">The live editable model type.</typeparam>
public interface ISparseEditSessionModelAccessor<out TModel>
    where TModel : class
{
    /// <summary>Gets the live model without invalidating the session's change cache.</summary>
    TModel GetModelForFrameworkAccess();
}
