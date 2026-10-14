using System.ComponentModel;

namespace SparseTypes;

/// <summary>A typed model path that can create an edit session rooted at its addressed model.</summary>
/// <typeparam name="TParent">The model containing the addressed child model.</typeparam>
/// <typeparam name="TChild">The addressed child model type.</typeparam>
/// <typeparam name="TChildPaths">The generated path builder rooted at the child model.</typeparam>
/// <typeparam name="TChildSession">The generated edit-session type for the child model.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseChildEditPath<TParent, TChild, TChildPaths, TChildSession>
    : ISparseTypedPath<TParent, TChild>
    where TChild : class
    where TChildPaths : ISparseTypedPath<TChild, TChild>
    where TChildSession : ISparseEditSessionWithPaths<TChild, TChildPaths>
{
    /// <summary>Creates the generated path builder rooted at the child model.</summary>
    TChildPaths CreateRootPaths();

    /// <summary>Creates an independent child-model snapshot.</summary>
    /// <param name="model">The child model to copy.</param>
    TChild Clone(TChild model);

    /// <summary>Creates a child edit session connected to the addressed model.</summary>
    /// <param name="baseline">The child model state to retain as the baseline.</param>
    /// <param name="current">The live child model to edit.</param>
    /// <param name="onChanged">Callback raised after an observable child edit.</param>
    TChildSession CreateEditSession(TChild baseline, TChild current, System.Action? onChanged);
}
