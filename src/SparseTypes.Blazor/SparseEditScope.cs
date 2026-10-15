using Microsoft.AspNetCore.Components.Forms;

namespace SparseTypes.Blazor;

/// <summary>The session scope cascaded by <see cref="SparseEdit{TModel, TPaths}"/> for field components.</summary>
/// <remarks>Keeps the scope independent of generated path types.</remarks>
/// <typeparam name="TModel">The live editable model type.</typeparam>
public interface ISparseEditScope<out TModel>
    where TModel : class
{
    /// <summary>Gets the active edit session.</summary>
    ISparseEditSession<TModel> Session { get; }

    /// <summary>Gets the original-model edit context bound to the session model.</summary>
    EditContext EditContext { get; }
}
