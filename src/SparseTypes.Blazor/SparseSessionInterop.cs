using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using SparseTypes.Generated;

namespace SparseTypes.Blazor;

/// <summary>Reflection bridge to the generated session members that stay outside the minimal session contract.</summary>
/// <remarks>Reflects over optional generated session members.</remarks>
internal static class SparseSessionInterop
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> HistoryProperties = new();
    private static readonly ConcurrentDictionary<Type, EventInfo?> HistoryChangedEvents = new();
    private static readonly ConcurrentDictionary<
        (Type Session, Type Value),
        MethodInfo?
    > FieldStateMethods = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo?> CollectionStateMethods = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> KeySegmentMethods = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo?> EnableHistoryMethods = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo?> DisableHistoryMethods = new();

    /// <summary>Enables the session-owned history used by a scope-owned child session.</summary>
    internal static void EnableHistory(object session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var method = EnableHistoryMethods.GetOrAdd(
            session.GetType(),
            static type =>
                type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(candidate =>
                    {
                        var parameters = candidate.GetParameters();
                        return candidate.Name == "EnableHistory"
                            && candidate.ReturnType == typeof(ISparseEditHistory)
                            && parameters.Length == 1
                            && parameters[0].IsOptional;
                    })
        );
        InvokeHistoryMethod(session, method, [null], "EnableHistory");
    }

    /// <summary>Releases session-owned history enabled for a scope-owned child session.</summary>
    internal static void DisableHistory(object session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var method = DisableHistoryMethods.GetOrAdd(
            session.GetType(),
            static type =>
                type.GetMethod(
                    "DisableHistory",
                    BindingFlags.Instance | BindingFlags.Public,
                    Type.EmptyTypes
                )
        );
        InvokeHistoryMethod(session, method, null, "DisableHistory");
    }

    /// <summary>Reads the session-owned history, or null when absent or not enabled.</summary>
    internal static ISparseEditHistory? TryGetHistory(object session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var property = HistoryProperties.GetOrAdd(
            session.GetType(),
            static type =>
            {
                var candidate = type.GetProperty(
                    "History",
                    BindingFlags.Instance | BindingFlags.Public
                );
                return
                    candidate is not null
                    && candidate.CanRead
                    && candidate.GetIndexParameters().Length == 0
                    && typeof(ISparseEditHistory).IsAssignableFrom(candidate.PropertyType)
                    ? candidate
                    : null;
            }
        );
        if (property is null)
        {
            return null;
        }

        try
        {
            return property.GetValue(session) as ISparseEditHistory;
        }
        catch (Exception exception)
            when (exception is TargetInvocationException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Subscribes to the session availability event, or null when the session has none.</summary>
    /// <param name="session">The edit session.</param>
    /// <param name="handler">The availability callback.</param>
    /// <returns>An unsubscriber, or null when the session exposes no such event.</returns>
    internal static Action? SubscribeHistoryChanged(object session, Action handler)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(handler);
        var sessionEvent = HistoryChangedEvents.GetOrAdd(
            session.GetType(),
            static type =>
            {
                var candidate = type.GetEvent(
                    "HistoryChanged",
                    BindingFlags.Instance | BindingFlags.Public
                );
                return candidate is not null && candidate.EventHandlerType == typeof(Action)
                    ? candidate
                    : null;
            }
        );
        if (sessionEvent is null)
        {
            return null;
        }

        try
        {
            sessionEvent.AddEventHandler(session, handler);
        }
        catch (Exception exception)
            when (exception
                    is ArgumentException
                        or TargetInvocationException
                        or InvalidOperationException
            )
        {
            return null;
        }

        return () =>
        {
            try
            {
                sessionEvent.RemoveEventHandler(session, handler);
            }
            catch (Exception exception)
                when (exception
                        is ArgumentException
                            or TargetInvocationException
                            or InvalidOperationException
                )
            {
                // Best-effort detach during teardown.
            }
        };
    }

    /// <summary>Queries the live semantic field state and preserves query failures for the UI.</summary>
    internal static bool TryFieldState<TModel, TValue>(
        ISparseEditSession<TModel> session,
        SparsePath<TModel, TValue> path,
        out SparseFieldState<TValue> state,
        out string? error
    )
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(session);
        var method = FieldStateMethods.GetOrAdd(
            (session.GetType(), typeof(TValue)),
            static key => FindFieldStateMethod(key.Session, key.Value)
        );
        if (method is null)
        {
            state = default;
            error = $"The session type '{session.GetType()}' does not provide FieldState.";
            return false;
        }

        try
        {
            state = (SparseFieldState<TValue>)method.Invoke(session, [path])!;
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            state = default;
            error = exception is TargetInvocationException { InnerException: { } inner }
                ? inner.Message
                : exception.Message;
            return false;
        }
    }

    /// <summary>Queries the live collection state for a root collection path.</summary>
    /// <returns>True with the view on success; false with a null view when unavailable.</returns>
    internal static bool TryCollectionState(
        object session,
        SparsePath path,
        out SparseCollectionState? state
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        var method = CollectionStateMethods.GetOrAdd(
            session.GetType(),
            static type =>
            {
                foreach (
                    var candidate in type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                )
                {
                    if (
                        candidate.Name != "CollectionState"
                        || candidate.IsGenericMethod
                        || candidate.ReturnType != typeof(SparseCollectionState)
                    )
                    {
                        continue;
                    }

                    var parameters = candidate.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(SparsePath))
                    {
                        return candidate;
                    }
                }

                return null;
            }
        );
        if (method is null)
        {
            state = null;
            return false;
        }

        try
        {
            state = (SparseCollectionState)method.Invoke(session, [path])!;
            return true;
        }
        catch (TargetInvocationException exception)
            when (exception.InnerException is ArgumentException)
        {
            // Not a root keyed or dictionary collection path; the caller
            // reports absence.
            state = null;
            return false;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System
                .Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException)
                .Throw();
            throw;
        }
    }

    internal static SparseCollectionEditor<TElement> CreateCollectionEditor<TElement>(
        object session,
        SparsePath path
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        var descriptorsProperty = session
            .GetType()
            .GetProperty("Descriptors", BindingFlags.Instance | BindingFlags.Public);
        if (
            descriptorsProperty?.CanRead != true
            || descriptorsProperty.GetValue(session)
                is not SparseTypes.Generated.IDescriptorSet descriptors
        )
        {
            throw new InvalidOperationException(
                $"The session type '{session.GetType()}' does not expose generated descriptors."
            );
        }

        var batchMethod = session
            .GetType()
            .GetMethod("BatchEdit", BindingFlags.Instance | BindingFlags.Public, [typeof(Action)]);
        if (batchMethod?.ReturnType != typeof(void))
        {
            throw new InvalidOperationException(
                $"The session type '{session.GetType()}' does not provide BatchEdit(Action)."
            );
        }

        return descriptors.EditCollection<TElement>(
            path,
            mutation =>
            {
                try
                {
                    batchMethod.Invoke(session, [mutation]);
                }
                catch (TargetInvocationException exception)
                    when (exception.InnerException is not null)
                {
                    System
                        .Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(
                            exception.InnerException
                        )
                        .Throw();
                    throw;
                }
            }
        );
    }

    internal static bool TryGetCollectionRowIdentity(
        object session,
        SparsePath path,
        int index,
        out SparsePathSegment identity,
        out object? key,
        out Guid? temporaryKey,
        out bool isKeyed,
        out string? error
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        identity = default;
        key = null;
        temporaryKey = null;
        isKeyed = false;

        var descriptorsProperty = session
            .GetType()
            .GetProperty("Descriptors", BindingFlags.Instance | BindingFlags.Public);
        if (
            descriptorsProperty?.CanRead != true
            || descriptorsProperty.GetValue(session) is not IDescriptorSet descriptors
        )
        {
            error =
                $"The session type '{session.GetType()}' does not expose generated descriptors.";
            return false;
        }

        var array = descriptors.Find(path)?.Array;
        if (array is null)
        {
            error = $"The path '{path}' does not resolve to a generated sequence descriptor.";
            return false;
        }

        if ((uint)index >= (uint)array.Count)
        {
            error = $"Collection row {index} is no longer present.";
            return false;
        }

        isKeyed = array.IsKeyed;
        if (!isKeyed)
        {
            identity = SparsePathSegment.At(index);
            error = null;
            return true;
        }

        key = array.GetItemKey(index);
        if (key is not null && !array.IsUnassignedKey(key))
        {
            var keyType = array.KeyType;
            if (keyType is null)
            {
                error = $"The keyed collection at '{path}' does not expose its key type.";
                return false;
            }

            identity = CreateKeySegment(keyType, key);
            error = null;
            return true;
        }

        var itemDescriptors = array.GetItemDescriptors(index);
        if (itemDescriptors is not null)
        {
            foreach (var descriptor in itemDescriptors.Members)
            {
                if (
                    !descriptor.Attributes.Any(attribute =>
                        attribute is SparseTemporaryKeyAttribute
                    )
                )
                {
                    continue;
                }

                var value = descriptor.GetModelValue();
                if (value is Guid temporaryGuid && temporaryGuid != Guid.Empty)
                {
                    temporaryKey = temporaryGuid;
                    identity = SparsePathSegment.TemporaryKeyOf(temporaryGuid);
                    error = null;
                    return true;
                }

                break;
            }
        }

        error =
            $"The keyed collection row at index {index} has neither an assigned key nor a declared temporary identity.";
        return false;
    }

    private static SparsePathSegment CreateKeySegment(Type keyType, object? key)
    {
        var factory = KeySegmentMethods.GetOrAdd(
            keyType,
            static type =>
                typeof(SparseSessionInterop)
                    .GetMethod(
                        nameof(CreateKeySegmentCore),
                        BindingFlags.Static | BindingFlags.Public
                    )!
                    .MakeGenericMethod(type)
        );
        try
        {
            return (SparsePathSegment)factory.Invoke(null, [key])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System
                .Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException)
                .Throw();
            throw;
        }
    }

    public static SparsePathSegment CreateKeySegmentCore<TKey>(object? key) =>
        SparsePathSegment.KeyOf((TKey)key!);

    private static MethodInfo? FindFieldStateMethod(Type sessionType, Type valueType)
    {
        foreach (
            var candidate in sessionType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
        )
        {
            if (
                candidate.Name != "FieldState"
                || !candidate.IsGenericMethodDefinition
                || candidate.GetGenericArguments().Length != 1
            )
            {
                continue;
            }

            var parameters = candidate.GetParameters();
            if (parameters.Length != 1 || !IsTypedPathParameter(parameters[0].ParameterType))
            {
                continue;
            }

            try
            {
                return candidate.MakeGenericMethod(valueType);
            }
            catch (Exception exception)
                when (exception is ArgumentException or InvalidOperationException)
            {
                return null;
            }
        }

        return null;
    }

    private static void InvokeHistoryMethod(
        object session,
        MethodInfo? method,
        object?[]? arguments,
        string methodName
    )
    {
        if (method is null)
        {
            throw new InvalidOperationException(
                $"The session type '{session.GetType()}' does not provide {methodName}."
            );
        }

        try
        {
            method.Invoke(session, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System
                .Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException)
                .Throw();
            throw;
        }
    }

    private static bool IsTypedPathParameter(Type parameter) =>
        parameter.IsGenericType && parameter.GetGenericTypeDefinition() == typeof(SparsePath<,>);
}
