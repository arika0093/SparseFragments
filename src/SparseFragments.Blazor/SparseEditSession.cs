using Microsoft.AspNetCore.Components.Forms;

namespace SparseFragments.Blazor;

/// <summary>Bridges ordinary Blazor forms and SparseFragments semantic patches.</summary>
/// <remarks>
/// The session keeps a fragment baseline alongside the live model and exposes an <see cref="EditContext"/>
/// for normal Blazor behavior. Changes are derived from the baseline-versus-current ChangeSet algebra,
/// never from <see cref="EditContext"/> modified fields.
/// </remarks>
/// <typeparam name="TModel">The editable model type. Must be a reference type.</typeparam>
/// <typeparam name="TFragment">The generated fragment type for <typeparamref name="TModel"/>.</typeparam>
/// <typeparam name="TPatch">The generated baseline-free patch type for <typeparamref name="TModel"/>.</typeparam>
/// <typeparam name="TChangeSet">The generated baseline-aware change set type for <typeparamref name="TModel"/>.</typeparam>
public sealed class SparseEditSession<TModel, TFragment, TPatch, TChangeSet>
    where TModel : class
    where TFragment : class
    where TPatch : class
    where TChangeSet : class
{
    private readonly Func<TModel, TFragment> _fromModel;
    private readonly Func<Optional<TFragment?>, Optional<TFragment?>, TChangeSet> _between;
    private readonly Func<TChangeSet, TPatch> _toPatch;
    private readonly Func<TChangeSet, bool> _isEmpty;
    private Optional<TFragment?> _baseline;

    private SparseEditSession(
        TModel model,
        Func<TModel, TFragment> fromModel,
        Func<Optional<TFragment?>, Optional<TFragment?>, TChangeSet> between,
        Func<TChangeSet, TPatch> toPatch,
        Func<TChangeSet, bool> isEmpty
    )
    {
        Model = model;
        EditContext = new EditContext(model);
        _fromModel = fromModel;
        _between = between;
        _toPatch = toPatch;
        _isEmpty = isEmpty;
        _baseline = Optional<TFragment?>.Present(fromModel(model));
    }

    /// <summary>Creates a session capturing the current model state as the baseline.</summary>
    public static SparseEditSession<TModel, TFragment, TPatch, TChangeSet> Create(
        TModel model,
        Func<TModel, TFragment> fromModel,
        Func<Optional<TFragment?>, Optional<TFragment?>, TChangeSet> between,
        Func<TChangeSet, TPatch> toPatch,
        Func<TChangeSet, bool> isEmpty
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(fromModel);
        ArgumentNullException.ThrowIfNull(between);
        ArgumentNullException.ThrowIfNull(toPatch);
        ArgumentNullException.ThrowIfNull(isEmpty);
        return new SparseEditSession<TModel, TFragment, TPatch, TChangeSet>(
            model,
            fromModel,
            between,
            toPatch,
            isEmpty
        );
    }

    /// <summary>The live editable model. The UI mutates this instance directly.</summary>
    public TModel Model { get; }

    /// <summary>The Blazor edit context for validation, field state, and submit behavior.</summary>
    public EditContext EditContext { get; }

    /// <summary>Whether the current model differs semantically from the baseline.</summary>
    public bool HasChanges => !_isEmpty(CreateChangeSet());

    /// <summary>Derives the baseline-aware change set between baseline and current model.</summary>
    public TChangeSet CreateChangeSet() =>
        _between(_baseline, Optional<TFragment?>.Present(_fromModel(Model)));

    /// <summary>Derives the baseline-free patch (<c>CreateChangeSet().ToPatch()</c>).</summary>
    public TPatch CreatePatch() => _toPatch(CreateChangeSet());

    /// <summary>Accepts the current model state as the new baseline.</summary>
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

    /// <summary>Surfaces a message through Blazor's <see cref="ValidationMessageStore"/>.</summary>
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
