namespace SparseTypes;

/// <summary>A typed edit-session contract that exposes generated model-specific path navigation.</summary>
/// <remarks>
/// The concrete <typeparamref name="TPaths"/> is the existing generated root
/// <c>SparsePaths&lt;TModel&gt;</c> builder for <typeparamref name="TModel"/>,
/// so navigation such as <c>session.Paths.Items.Key(id).Title</c> keeps
/// compile-time root, value, and key typing without a separate root builder.
/// <c>ISparseEditSession&lt;TModel&gt;</c> stays the minimal fallback;
/// this interface is the opt-in typed provider for framework-neutral
/// consumers. Generated child-model sessions expose their own concrete path
/// builder through this interface as well.
/// </remarks>
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
