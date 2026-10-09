namespace SparseFragments;

/// <summary>Trusted read-only access to a session's live model for framework integrations.</summary>
/// <remarks>
/// Unlike <see cref="ISparseEditSession{TModel}.Model"/>, this access preserves the
/// session's observable-change cache. It is intended for framework helpers that only
/// need model identity or path resolution (such as Blazor edit contexts and field
/// identifiers). Callers must not mutate the returned instance or publish it to code
/// that might; mutating without observable notifications leaves cached change state
/// stale. Sessions that do not implement this interface keep working: framework
/// helpers fall back to <see cref="ISparseEditSession{TModel}.Model"/>.
/// </remarks>
/// <typeparam name="TModel">The live editable model type.</typeparam>
public interface ISparseEditSessionModelAccessor<out TModel>
    where TModel : class
{
    /// <summary>Gets the live model without invalidating the session's change cache.</summary>
    TModel GetModelForFrameworkAccess();
}
