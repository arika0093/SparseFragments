namespace SparseTypes;

/// <summary>A typed edit-session contract that exposes generated model-specific path navigation.</summary>
/// <remarks><typeparamref name="TPaths"/> is the generated root path builder type.</remarks>
/// <typeparam name="TModel">The live editable model type.</typeparam>
/// <typeparam name="TPaths">The concrete generated root path builder type.</typeparam>
public interface ISparseEditSessionWithPaths<out TModel, out TPaths> : ISparseEditSession<TModel>
    where TModel : class
    where TPaths : ISparseTypedPath<TModel, TModel>
{
    /// <summary>Gets the typed path navigation rooted at this session's model.</summary>
    /// <remarks>
    /// Path-only access: reading this property never reads the mutable model,
    /// invalidates the change cache, or subscribes to edit notifications.
    /// </remarks>
    TPaths Paths { get; }
}
