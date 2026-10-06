using Microsoft.AspNetCore.Components.Forms;

namespace SparseFragments.Blazor;

/// <summary>
/// Bridges ordinary Blazor forms and SparseFragments semantic patches.
/// </summary>
/// <remarks>
/// <para>
/// The session keeps a SparseFragments baseline (captured as a fragment) alongside the
/// live editable model and exposes an <see cref="EditContext"/> for normal Blazor form
/// behavior (validation, field-modified state, change notifications, submit).
/// </para>
/// <para>
/// Patches are always derived from baseline-versus-current state through the generated
/// fragment/patch algebra — never reconstructed from <see cref="EditContext"/> modified
/// fields — so collection add/remove/reorder, nested edits, and edit-then-restore cases
/// are represented correctly even when Blazor emits no equivalent field notification.
/// </para>
/// <para>
/// This package depends only on SparseFragments and Blazor forms abstractions. It does
/// not require ASP.NET Core server integration or an HTTP transport package.
/// </para>
/// </remarks>
/// <typeparam name="TModel">The editable model type. Must be a reference type.</typeparam>
/// <typeparam name="TFragment">The generated fragment type for <typeparamref name="TModel"/>.</typeparam>
/// <typeparam name="TPatch">The generated patch type for <typeparamref name="TModel"/>.</typeparam>
public sealed class SparseEditSession<TModel, TFragment, TPatch>
    where TModel : class
    where TFragment : class
{
    private readonly Func<TModel, TFragment> _fromModel;
    private readonly Func<Optional<TFragment?>, Optional<TFragment?>, TPatch> _between;
    private readonly Func<TPatch, bool> _isEmpty;
    private Optional<TFragment?> _baseline;

    private SparseEditSession(
        TModel model,
        Func<TModel, TFragment> fromModel,
        Func<Optional<TFragment?>, Optional<TFragment?>, TPatch> between,
        Func<TPatch, bool> isEmpty
    )
    {
        Model = model;
        EditContext = new EditContext(model);
        _fromModel = fromModel;
        _between = between;
        _isEmpty = isEmpty;
        _baseline = Optional<TFragment?>.Present(fromModel(model));
    }

    /// <summary>Creates a session capturing the current model state as the baseline.</summary>
    public static SparseEditSession<TModel, TFragment, TPatch> Create(
        TModel model,
        Func<TModel, TFragment> fromModel,
        Func<Optional<TFragment?>, Optional<TFragment?>, TPatch> between,
        Func<TPatch, bool> isEmpty
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(fromModel);
        ArgumentNullException.ThrowIfNull(between);
        ArgumentNullException.ThrowIfNull(isEmpty);
        return new SparseEditSession<TModel, TFragment, TPatch>(model, fromModel, between, isEmpty);
    }

    /// <summary>The live editable model. The UI mutates this instance directly.</summary>
    public TModel Model { get; }

    /// <summary>The Blazor edit context for validation, field state, and submit behavior.</summary>
    public EditContext EditContext { get; }

    /// <summary>
    /// Whether the current model differs semantically from the baseline.
    /// Always computed from the fragment algebra; collection mutations without
    /// Blazor field notifications are still detected.
    /// </summary>
    public bool HasChanges => !_isEmpty(CreatePatch());

    /// <summary>Derives the semantic patch between the baseline and the current model.</summary>
    public TPatch CreatePatch() =>
        _between(_baseline, Optional<TFragment?>.Present(_fromModel(Model)));

    /// <summary>
    /// Accepts the current model state: replaces the baseline, clears Blazor modified
    /// flags, and keeps the same model instance and <see cref="EditContext"/>.
    /// </summary>
    public void AcceptChanges()
    {
        _baseline = Optional<TFragment?>.Present(_fromModel(Model));
        EditContext.MarkAsUnmodified();
    }

    /// <summary>Creates a validation store bound to this session's <see cref="EditContext"/>.</summary>
    public ValidationMessageStore CreateValidationStore() => new(EditContext);

    /// <summary>Resolves a Blazor field identifier for a model member name.</summary>
    public FieldIdentifier Field(string fieldName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        return new FieldIdentifier(Model, fieldName);
    }

    /// <summary>
    /// Surfaces a message (for example a server-side or conflict error associated with a
    /// member path) through Blazor's <see cref="ValidationMessageStore"/>.
    /// </summary>
    public static void AddValidationError(
        ValidationMessageStore store,
        FieldIdentifier field,
        string message
    )
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(message);
        store.Add(field, message);
    }
}
