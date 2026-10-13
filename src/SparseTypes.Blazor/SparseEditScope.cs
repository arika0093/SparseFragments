using Microsoft.AspNetCore.Components.Forms;

namespace SparseTypes.Blazor;

/// <summary>
/// The session scope cascaded by <see cref="SparseEdit{TModel, TPaths}"/> for field components.
/// </summary>
/// <remarks>
/// Carries the active session and its original-model <see cref="EditContext"/> without naming
/// the concrete generated path builder, so <see cref="SparseField{TModel, TValue}"/> can stay
/// generic over the model and value only. The scope instance is replaced whenever the session
/// is replaced; it never owns the session, the context, or the session-owned history.
/// </remarks>
/// <typeparam name="TModel">The live editable model type.</typeparam>
public interface ISparseEditScope<out TModel>
    where TModel : class
{
    /// <summary>Gets the active edit session.</summary>
    ISparseEditSession<TModel> Session { get; }

    /// <summary>Gets the original-model edit context bound to the session model.</summary>
    EditContext EditContext { get; }
}
