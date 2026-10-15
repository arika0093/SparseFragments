using System;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.Components.Forms;
using SparseTypes;
using SparseTypes.Generated;

namespace SparseTypes.Blazor;

/// <summary>Options controlling a typed field binding.</summary>
public sealed record SparseFieldBindingOptions
{
    /// <summary>Gets whether writes run inside the session batch runner when available.</summary>
    public bool UseBatchEdit { get; init; }

    /// <summary>Gets the initial disabled state.</summary>
    public bool Disabled { get; init; }
}

/// <summary>Toolkit-agnostic typed binding over one session path and one original-model edit context.</summary>
/// <remarks>Reads resolve live on every access so nested replacement is followed.</remarks>
/// <typeparam name="TModel">The session model type.</typeparam>
/// <typeparam name="TValue">The addressed value type.</typeparam>
public sealed class SparseFieldBinding<TModel, TValue>
    where TModel : class
{
    private readonly ISparseEditSession<TModel> _session;
    private readonly EditContext _editContext;
    private readonly SparsePath<TModel, TValue> _typedPath;
    private readonly SparsePath _path;
    private readonly IDescriptorSet? _descriptors;
    private readonly ISparseEditSessionWithBatchEdit? _batch;
    private object? _expressionRoot;
    private Expression<Func<TValue>>? _cachedExpression;

    internal SparseFieldBinding(
        ISparseEditSession<TModel> session,
        EditContext editContext,
        SparsePath<TModel, TValue> typedPath,
        SparseFieldBindingOptions? options
    )
    {
        _session = session;
        _editContext = editContext;
        _typedPath = typedPath;
        _path = typedPath.Path;
        Disabled = options?.Disabled == true;
        _descriptors = TryGetDescriptors(session);
        _batch = options?.UseBatchEdit == true ? session as ISparseEditSessionWithBatchEdit : null;
    }

    internal SparseFieldBinding(
        ISparseEditSession<TModel> session,
        EditContext editContext,
        SparsePath path,
        SparseFieldBindingOptions? options
    )
    {
        _session = session;
        _editContext = editContext;
        _typedPath = default;
        _path = path;
        Disabled = options?.Disabled == true;
        _descriptors = TryGetDescriptors(session);
        _batch = options?.UseBatchEdit == true ? session as ISparseEditSessionWithBatchEdit : null;
    }

    /// <summary>Gets the typed path when bound through one; otherwise the default root path.</summary>
    public SparsePath<TModel, TValue> TypedPath => _typedPath;

    /// <summary>Gets the canonical model-agnostic path.</summary>
    public SparsePath UntypedPath => _path;

    /// <summary>Gets the captured original-model edit context.</summary>
    public EditContext EditContext => _editContext;

    /// <summary>Gets or sets whether writes are blocked.</summary>
    public bool Disabled { get; set; }

    /// <summary>Gets whether the path no longer resolves or the context left the session model.</summary>
    public bool IsStale => !TryGetField(out _, out _);

    /// <summary>Gets whether the addressed member is read-only.</summary>
    public bool IsReadOnly => FindLeaf() is { } leaf && leaf.IsReadOnly;

    /// <summary>Gets whether the addressed member accepts null.</summary>
    public bool IsNullable
    {
        get
        {
            if (FindLeaf() is { } leaf)
            {
                return leaf.IsNullable || leaf.IsNullableOblivious;
            }

            var target = typeof(TValue);
            return !target.IsValueType || Nullable.GetUnderlyingType(target) is not null;
        }
    }

    /// <summary>Gets the live field identifier, targeting the original model graph.</summary>
    /// <exception cref="InvalidOperationException">The binding is stale.</exception>
    public FieldIdentifier Field
    {
        get
        {
            if (!TryGetField(out var fieldId, out var error))
            {
                throw new InvalidOperationException(error);
            }

            return fieldId;
        }
    }

    /// <summary>Gets the live typed value.</summary>
    /// <exception cref="InvalidOperationException">The binding is stale or mistyped.</exception>
    public TValue Value
    {
        get
        {
            if (!TryGetValue(out var value, out var error))
            {
                throw new InvalidOperationException(error);
            }

            return value!;
        }
    }

    /// <summary>Gets a value expression targeting the original model for <c>InputBase</c> use.</summary>
    /// <remarks>Only member paths expose value expressions; collection paths return null instead of a fabricated expression.</remarks>
    public Expression<Func<TValue>>? ValueExpression => TryBuildValueExpression();

    /// <summary>Attempts to read the live typed value.</summary>
    /// <param name="value">The current value on success.</param>
    /// <param name="error">The failure reason on failure.</param>
    /// <returns>True when the value resolved and matched <typeparamref name="TValue"/>.</returns>
    public bool TryGetValue(out TValue? value, out string? error)
    {
        if (!ValidateContext(out error))
        {
            value = default;
            return false;
        }

        if (_descriptors is not null)
        {
            var leaf = FindLeaf();
            if (leaf is null)
            {
                error = $"The field '{_path}' no longer resolves to a live model member.";
                value = default;
                return false;
            }

            object? raw;
            try
            {
                raw = leaf.GetModelValue();
            }
            catch (Exception exception)
                when (exception is InvalidOperationException or ObjectDisposedException)
            {
                error = exception.Message;
                value = default;
                return false;
            }

            return Unbox(raw, out value, out error);
        }

        if (!TryReadFallback(out var fallbackRaw, out error))
        {
            value = default;
            return false;
        }

        return Unbox(fallbackRaw, out value, out error);
    }

    /// <summary>Attempts to resolve the live field identifier.</summary>
    /// <param name="fieldId">The field on success.</param>
    /// <param name="error">The failure reason on failure.</param>
    /// <returns>True when the path still resolves.</returns>
    public bool TryGetField(out FieldIdentifier fieldId, out string? error)
    {
        if (!ValidateContext(out error))
        {
            fieldId = default;
            return false;
        }

        try
        {
            fieldId = SparseFieldPathResolver.Resolve(GetSessionModel(_session), _path);
            error = null;
            return true;
        }
        catch (ArgumentException exception)
        {
            fieldId = default;
            error = exception.Message;
            return false;
        }
    }

    /// <summary>Attempts to write through the observable path and notify the edit context.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="error">The failure reason on failure.</param>
    /// <returns>True when the write and notification succeeded.</returns>
    public bool TrySet(TValue? value, out string? error)
    {
        if (Disabled)
        {
            error = "The binding is disabled.";
            return false;
        }

        if (!ValidateContext(out error))
        {
            return false;
        }

        if (_descriptors is not null)
        {
            var leaf = FindLeaf();
            if (leaf is null)
            {
                error = $"The field '{_path}' no longer resolves to a live model member.";
                return false;
            }

            if (leaf.IsReadOnly)
            {
                error = $"The field '{_path}' is read-only.";
                return false;
            }

            bool written;
            try
            {
                written = _batch is not null ? InvokeBatched(leaf, value) : leaf.TrySetValue(value);
            }
            catch (Exception exception)
                when (exception
                        is InvalidOperationException
                            or ObjectDisposedException
                            or ArgumentException
                )
            {
                error = exception.Message;
                return false;
            }

            if (!written)
            {
                error = DescribeWriteFailure(value);
                return false;
            }

            return TryNotify(out error);
        }

        return TrySetFallback(value, out error);
    }

    /// <summary>Writes through the binding, throwing on failure.</summary>
    /// <param name="value">The value to write.</param>
    /// <exception cref="InvalidOperationException">The write failed; the message carries the reason.</exception>
    public void Set(TValue? value)
    {
        if (!TrySet(value, out var error))
        {
            throw new InvalidOperationException(error);
        }
    }

    /// <summary>EventCallback-compatible write entry point.</summary>
    /// <param name="value">The value to write.</param>
    /// <exception cref="InvalidOperationException">The write failed.</exception>
    public Task SetAsync(TValue? value)
    {
        Set(value);
        return Task.CompletedTask;
    }

    private bool TryNotify(out string? error)
    {
        if (!TryGetField(out var fieldId, out error))
        {
            return false;
        }

        _editContext.NotifyFieldChanged(fieldId);
        error = null;
        return true;
    }

    private bool TrySetFallback(TValue? value, out string? error)
    {
        if (!TryGetField(out var fieldId, out error))
        {
            return false;
        }

        if (
            !SparseTypeMetadataCache.TryGetProperty(
                fieldId.Model.GetType(),
                fieldId.FieldName,
                out var property
            )
            || property is null
            || !property.CanWrite
        )
        {
            error = $"The field '{_path}' is read-only.";
            return false;
        }

        if (
            value is null
            && property.PropertyType.IsValueType
            && Nullable.GetUnderlyingType(property.PropertyType) is null
        )
        {
            error = $"The field '{_path}' does not accept null.";
            return false;
        }

        if (value is not null && !property.PropertyType.IsInstanceOfType(value))
        {
            error = $"The value cannot be written to '{_path}'.";
            return false;
        }

        try
        {
            if (_batch is not null)
            {
                Exception? captured = null;
                _batch.BatchEdit(
                    new Action(() =>
                    {
                        try
                        {
                            property.SetValue(fieldId.Model, value);
                        }
                        catch (Exception exception)
                        {
                            captured = exception;
                        }
                    })
                );
                if (captured is not null)
                {
                    error = captured.Message;
                    return false;
                }
            }
            else
            {
                property.SetValue(fieldId.Model, value);
            }
        }
        catch (Exception exception)
            when (exception
                    is TargetInvocationException
                        or ArgumentException
                        or InvalidOperationException
                        or ObjectDisposedException
            )
        {
            error = exception.InnerException?.Message ?? exception.Message;
            return false;
        }

        return TryNotify(out error);
    }

    private bool TryReadFallback(out object? raw, out string? error)
    {
        if (!TryGetField(out var fallbackId, out error))
        {
            raw = null;
            return false;
        }

        if (
            !SparseTypeMetadataCache.TryGetProperty(
                fallbackId.Model.GetType(),
                fallbackId.FieldName,
                out var property
            )
            || property is null
            || !property.CanRead
        )
        {
            error = $"The field '{_path}' no longer resolves to a live model member.";
            raw = null;
            return false;
        }

        try
        {
            raw = property.GetValue(fallbackId.Model);
        }
        catch (Exception exception)
            when (exception
                    is TargetInvocationException
                        or InvalidOperationException
                        or ObjectDisposedException
            )
        {
            error = exception.InnerException?.Message ?? exception.Message;
            raw = null;
            return false;
        }

        error = null;
        return true;
    }

    private static bool Unbox(object? raw, out TValue? value, out string? error)
    {
        if (raw is TValue typed)
        {
            value = typed;
            error = null;
            return true;
        }

        if (raw is null && default(TValue) is null)
        {
            value = default;
            error = null;
            return true;
        }

        value = default;
        error = "The value cannot be represented as the bound type.";
        return false;
    }

    private string DescribeWriteFailure(TValue? value)
    {
        if (!TryGetField(out _, out _))
        {
            return $"The field '{_path}' no longer resolves to a live model member.";
        }

        if (value is null)
        {
            return $"The field '{_path}' does not accept null.";
        }

        return $"The value could not be written to '{_path}'.";
    }

    private bool ValidateContext(out string? error)
    {
        TModel current;
        try
        {
            current = GetSessionModel(_session);
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or ObjectDisposedException)
        {
            error = exception.Message;
            return false;
        }

        if (!ReferenceEquals(_editContext.Model, current))
        {
            error = "The EditContext is no longer bound to the session's model.";
            return false;
        }

        error = null;
        return true;
    }

    private IDescriptor? FindLeaf()
    {
        var set = _descriptors;
        if (set is null)
        {
            return null;
        }

        try
        {
            return set.Find(_path);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private Expression<Func<TValue>>? TryBuildValueExpression()
    {
        foreach (var segment in _path.Segments)
        {
            if (segment.Kind != SparsePathSegmentKind.Member)
            {
                return null;
            }
        }

        if (!ValidateContext(out _))
        {
            return null;
        }

        TModel live;
        try
        {
            live = GetSessionModel(_session);
        }
        catch (Exception)
        {
            return null;
        }

        // Member-only trees stay valid across nested replacement: intermediate
        // accesses re-evaluate from the root on each read. Reuse the tree while
        // the root identity holds; rebuild when the root changes.
        if (_cachedExpression is not null && ReferenceEquals(live, _expressionRoot))
        {
            return _cachedExpression;
        }

        Expression current = Expression.Constant(live, typeof(TModel));
        var currentType = typeof(TModel);
        foreach (var segment in _path.Segments)
        {
            if (
                !SparseTypeMetadataCache.TryGetProperty(currentType, segment.Name, out var property)
                || property is null
                || !property.CanRead
            )
            {
                return null;
            }

            try
            {
                current = Expression.Property(current, property);
            }
            catch (ArgumentException)
            {
                return null;
            }

            currentType = property.PropertyType;
        }

        if (!typeof(TValue).IsAssignableFrom(currentType))
        {
            return null;
        }

        try
        {
            var built = Expression.Lambda<Func<TValue>>(current);
            _expressionRoot = live;
            _cachedExpression = built;
            return built;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private bool InvokeBatched(IDescriptor leaf, TValue? value)
    {
        var batch = _batch;
        if (batch is null)
        {
            return leaf.TrySetValue(value);
        }

        var written = false;
        Exception? captured = null;
        batch.BatchEdit(
            new Action(() =>
            {
                try
                {
                    written = leaf.TrySetValue(value);
                }
                catch (Exception exception)
                {
                    captured = exception;
                }
            })
        );
        if (captured is not null)
        {
            throw captured;
        }

        return written;
    }

    private static TModel GetSessionModel(ISparseEditSession<TModel> session) =>
        session is ISparseEditSessionModelAccessor<TModel> accessor
            ? accessor.GetModelForFrameworkAccess()
            : session.Model;

    private static IDescriptorSet? TryGetDescriptors(ISparseEditSession<TModel> session) =>
        // Typed capability probe: sessions without generated descriptors stay
        // on the model-property fallback instead of failing.
        (session as ISparseEditSessionWithDescriptors)?.Descriptors;
}
