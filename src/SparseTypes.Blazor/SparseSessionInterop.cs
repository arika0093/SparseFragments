using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;

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

    /// <summary>Queries the live semantic field state, defaulting to clean when unavailable.</summary>
    internal static SparseFieldState<TValue> FieldState<TModel, TValue>(
        ISparseEditSession<TModel> session,
        SparsePath<TModel, TValue> path
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
            return default;
        }

        try
        {
            return (SparseFieldState<TValue>)method.Invoke(session, [path])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System
                .Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException)
                .Throw();
            throw;
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
