using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace SparseTypes.Blazor;

/// <summary>The render-scoped template context provided by <see cref="SparseField{TModel, TValue}"/>.</summary>
/// <remarks>Wraps one live <see cref="SparseFieldBinding{TModel, TValue}"/> with component-friendly access.</remarks>
/// <typeparam name="TModel">The root session model type.</typeparam>
/// <typeparam name="TValue">The addressed value type.</typeparam>
public sealed class SparseFieldContext<TModel, TValue>
    where TModel : class
{
    private readonly ISparseEditSession<TModel> _session;
    private readonly SparseSessionScopeContext<TModel>? _scopeContext;

    internal SparseFieldContext(
        ComponentBase receiver,
        ISparseEditSession<TModel> session,
        SparseSessionScopeContext<TModel>? scopeContext,
        SparseFieldBinding<TModel, TValue> binding,
        ISparseTypedPath<TModel, TValue> path
    )
    {
        ArgumentNullException.ThrowIfNull(receiver);
        _session = session;
        _scopeContext = scopeContext;
        Binding = binding;
        Path = path;
        ValueChanged = EventCallback.Factory.Create<TValue>(
            receiver,
            value => binding.SetAsync(value)
        );
    }

    /// <summary>Gets the live typed binding backing this field.</summary>
    public SparseFieldBinding<TModel, TValue> Binding { get; }

    /// <summary>Gets the path this field was created from.</summary>
    /// <remarks>The runtime instance is the concrete generated builder, but the static type is the shared typed-path contract.</remarks>
    public ISparseTypedPath<TModel, TValue> Path { get; }

    /// <summary>Gets or sets the live typed value.</summary>
    /// <remarks>The setter writes through the binding and throws when the write fails (stale.</remarks>
    /// <exception cref="InvalidOperationException">The read or write failed.</exception>
    public TValue Value
    {
        get => Binding.Value;
        set => Binding.Set(value);
    }

    /// <summary>Gets the change callback writing through the binding.</summary>
    /// <remarks>Received by the owning field component, so invoking it refreshes the field after the write.</remarks>
    public EventCallback<TValue> ValueChanged { get; }

    /// <summary>Gets a value expression targeting the original model for <c>InputBase</c> use.</summary>
    /// <remarks>Only member-only paths expose an expression.</remarks>
    public Expression<Func<TValue>>? ValueExpression => Binding.ValueExpression;

    /// <summary>Gets the render-scoped semantic field state.</summary>
    /// <remarks>Snapshot by the owning field component once per render from a fresh session diff; it does not update until the next render.</remarks>
    public SparseFieldState<TValue> State { get; internal set; }

    /// <summary>Gets the reason the semantic state query failed, or null when it succeeded.</summary>
    public string? StateQueryError { get; internal set; }

    /// <summary>Gets whether the semantic state query failed during the latest render.</summary>
    public bool HasStateQueryError => StateQueryError is not null;

    /// <summary>Gets whether the path itself changed between the baseline and the live model.</summary>
    public bool IsChanged => State.IsChanged;

    /// <summary>Gets the presence-aware value before the exact transition.</summary>
    public Optional<TValue> Before => State.Before;

    /// <summary>Gets the presence-aware value after the exact transition.</summary>
    public Optional<TValue> After => State.After;

    /// <summary>Gets whether the addressed member is read-only.</summary>
    public bool IsReadOnly => Binding.IsReadOnly;

    /// <summary>Gets whether the path no longer resolves or the context left the session model.</summary>
    public bool IsStale => Binding.IsStale;

    /// <summary>Gets the live field identifier, targeting the original model graph.</summary>
    /// <exception cref="InvalidOperationException">The binding is stale.</exception>
    public FieldIdentifier Field => Binding.Field;

    /// <summary>Gets whether Blazor tracks the live field as modified.</summary>
    /// <remarks>False when the binding is stale; Blazor touched state is independent of the semantic <see cref="IsChanged"/>.</remarks>
    public bool IsModified =>
        Binding.TryGetField(out var fieldId, out _) && Binding.EditContext.IsModified(fieldId);

    /// <summary>Gets the current validation messages for the live field.</summary>
    /// <remarks>Empty when the binding is stale.</remarks>
    public IEnumerable<string> ValidationMessages =>
        Binding.TryGetField(out var fieldId, out _)
            ? Binding.EditContext.GetValidationMessages(fieldId)
            : [];

    /// <summary>Writes through the binding, throwing on failure.</summary>
    /// <param name="value">The value to write.</param>
    /// <exception cref="InvalidOperationException">The write failed.</exception>
    public Task SetAsync(TValue? value) => Binding.SetAsync(value);

    /// <summary>Attempts to write through the binding.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="error">The failure reason on failure.</param>
    /// <returns>True when the write and notification succeeded.</returns>
    public bool TrySet(TValue? value, out string? error) => Binding.TrySet(value, out error);

    /// <summary>Attempts to read the live typed value.</summary>
    /// <param name="value">The current value on success.</param>
    /// <param name="error">The failure reason on failure.</param>
    /// <returns>True when the value resolved and matched <typeparamref name="TValue"/>.</returns>
    public bool TryGetValue(out TValue? value, out string? error) =>
        Binding.TryGetValue(out value, out error);

    /// <summary>Queries the live collection state when this field addresses a root collection.</summary>
    /// <param name="state">The point-in-time view on success.</param>
    /// <returns>True when the path addresses a root keyed or dictionary collection.</returns>
    public bool TryGetCollectionState(out SparseCollectionState? state) =>
        _scopeContext?.OwnsSession(_session) == true
            ? _scopeContext.TryCollectionState(Path.UntypedPath, out state)
            : SparseSessionInterop.TryCollectionState(_session, Path.UntypedPath, out state);

    /// <summary>Navigates to a child member with a root-preserving compile-time typed path.</summary>
    /// <remarks>The selector must contain only member accesses.</remarks>
    /// <param name="selector">The member chain from the field value to the child.</param>
    /// <typeparam name="TChild">The addressed child type.</typeparam>
    /// <returns>A typed path usable as a nested <c>SparseField</c> path.</returns>
    /// <exception cref="ArgumentException">The selector is not a member-access chain.</exception>
    public SparsePath<TModel, TChild> Select<TChild>(Expression<Func<TValue, TChild>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new SparsePath<TModel, TChild>(
            AppendSelector(Path.UntypedPath, typeof(TValue), selector, nameof(selector))
        );
    }

    /// <summary>Navigates to a member of a keyed entry of a collection field.</summary>
    /// <remarks>The key keeps its compile-time type so the segment matches the generated spelling; boxing the key would silently miss.</remarks>
    /// <param name="key">The stable entry key.</param>
    /// <param name="selector">The member chain from the entry to the child.</param>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <typeparam name="TChild">The addressed child type.</typeparam>
    /// <returns>A typed path usable as a nested <c>SparseField</c> path.</returns>
    public SparsePath<TModel, TChild> Key<TKey, TElement, TChild>(
        TKey key,
        Expression<Func<TElement, TChild>> selector
    )
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        ArgumentNullException.ThrowIfNull(selector);
        RequireElement(typeof(TValue), typeof(TElement), nameof(TElement));
        return new SparsePath<TModel, TChild>(
            AppendSelector(Path.UntypedPath.Key(key), typeof(TElement), selector, nameof(selector))
        );
    }

    /// <summary>Navigates to a member of the entry at a position of a sequence field.</summary>
    /// <param name="index">The zero-based position.</param>
    /// <param name="selector">The member chain from the entry to the child.</param>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <typeparam name="TChild">The addressed child type.</typeparam>
    /// <returns>A typed path usable as a nested <c>SparseField</c> path.</returns>
    public SparsePath<TModel, TChild> At<TElement, TChild>(
        int index,
        Expression<Func<TElement, TChild>> selector
    )
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        ArgumentNullException.ThrowIfNull(selector);
        RequireElement(typeof(TValue), typeof(TElement), nameof(TElement));
        return new SparsePath<TModel, TChild>(
            AppendSelector(Path.UntypedPath.At(index), typeof(TElement), selector, nameof(selector))
        );
    }

    /// <summary>Navigates to a member of an unassigned keyed entry by its temporary identity.</summary>
    /// <param name="temporaryKey">The stable temporary identity.</param>
    /// <param name="selector">The member chain from the entry to the child.</param>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <typeparam name="TChild">The addressed child type.</typeparam>
    /// <returns>A typed path usable as a nested <c>SparseField</c> path.</returns>
    public SparsePath<TModel, TChild> TemporaryKey<TElement, TChild>(
        Guid temporaryKey,
        Expression<Func<TElement, TChild>> selector
    )
    {
        if (temporaryKey == Guid.Empty)
        {
            throw new ArgumentException(
                "The temporary key must not be empty.",
                nameof(temporaryKey)
            );
        }

        ArgumentNullException.ThrowIfNull(selector);
        RequireElement(typeof(TValue), typeof(TElement), nameof(TElement));
        return new SparsePath<TModel, TChild>(
            AppendSelector(
                Path.UntypedPath.TemporaryKey(temporaryKey),
                typeof(TElement),
                selector,
                nameof(selector)
            )
        );
    }

    internal static SparsePath AppendSelector<TElement, TChild>(
        SparsePath basePath,
        Type elementType,
        Expression<Func<TElement, TChild>> selector,
        string paramName
    )
    {
        var untyped = basePath;
        var current = elementType;
        foreach (var name in ReadMemberChain(selector, paramName))
        {
            var property = Nullable.GetUnderlyingType(current) ?? current;
            var candidate = property.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (
                candidate is null
                || !candidate.CanRead
                || candidate.GetIndexParameters().Length > 0
            )
            {
                throw new ArgumentException(
                    $"The member '{name}' does not resolve to a readable model member.",
                    paramName
                );
            }

            untyped = untyped.Member(name);
            current = Nullable.GetUnderlyingType(candidate.PropertyType) ?? candidate.PropertyType;
        }

        return untyped;
    }

    private static List<string> ReadMemberChain<TElement, TChild>(
        Expression<Func<TElement, TChild>> selector,
        string paramName
    )
    {
        var names = new List<string>();
        Expression node = selector.Body;
        while (true)
        {
            if (
                node is UnaryExpression unary
                && (
                    unary.NodeType == ExpressionType.Convert
                    || unary.NodeType == ExpressionType.ConvertChecked
                )
            )
            {
                node = unary.Operand;
                continue;
            }

            if (node is MemberExpression member && member.Expression is not null)
            {
                if (
                    member.Member is not PropertyInfo property
                    || property.GetIndexParameters().Length > 0
                )
                {
                    throw new ArgumentException(
                        "The selector must be a member-access chain over readable properties.",
                        paramName
                    );
                }

                names.Add(property.Name);
                node = member.Expression;
                continue;
            }

            if (node is ParameterExpression)
            {
                if (names.Count == 0)
                {
                    throw new ArgumentException(
                        "The selector must address a child member, not the field itself.",
                        paramName
                    );
                }

                names.Reverse();
                return names;
            }

            throw new ArgumentException(
                "The selector must be a member-access chain over the field value.",
                paramName
            );
        }
    }

    private static void RequireElement(Type collection, Type element, string paramName)
    {
        foreach (var candidate in collection.GetInterfaces())
        {
            if (!candidate.IsGenericType)
            {
                continue;
            }

            var definition = candidate.GetGenericTypeDefinition();
            var arguments = candidate.GetGenericArguments();
            if (
                (
                    definition == typeof(IEnumerable<>)
                    || definition == typeof(ICollection<>)
                    || definition == typeof(IList<>)
                    || definition == typeof(IReadOnlyList<>)
                    || definition == typeof(IReadOnlyCollection<>)
                ) && element.IsAssignableFrom(arguments[0])
            )
            {
                return;
            }

            if (
                (
                    definition == typeof(IDictionary<,>)
                    || definition == typeof(IReadOnlyDictionary<,>)
                ) && element.IsAssignableFrom(arguments[1])
            )
            {
                return;
            }
        }

        if (collection.IsArray && element.IsAssignableFrom(collection.GetElementType()))
        {
            return;
        }

        throw new ArgumentException(
            $"The field value '{collection}' is not a collection of '{element}'. "
                + "Select the collection member first.",
            paramName
        );
    }
}
