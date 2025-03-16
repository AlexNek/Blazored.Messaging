using System.Diagnostics;

namespace Blazor.Messaging;

public class MessageSubscriberManager
{
    private readonly Dictionary<Type, List<(string SubscriberInfo, Func<object, Task> Handler)>> _asyncSubscribers = new();
    private readonly Dictionary<Type, List<(string SubscriberInfo, Action<object> Handler)>> _syncSubscribers = new();

    public void Subscribe<TMessage>(Action<TMessage> handler) where TMessage : class
    {
        var messageType = typeof(TMessage);
        string subscriberInfo = GetSubscriberInfo();
        Action<object> wrappedHandler = msg => handler((TMessage)msg);

        lock (_syncSubscribers)
        {
            if (!_syncSubscribers.ContainsKey(messageType))
            {
                _syncSubscribers[messageType] = new List<(string, Action<object>)>();
            }

            if (!_syncSubscribers[messageType].Any(s => s.SubscriberInfo == subscriberInfo))
            {
                _syncSubscribers[messageType].Add((subscriberInfo, wrappedHandler));
            }
        }
    }

    public void Subscribe<TMessage>(Func<TMessage, Task> handler) where TMessage : class
    {
        var messageType = typeof(TMessage);
        string subscriberInfo = GetSubscriberInfo();
        Func<object, Task> wrappedHandler = msg => handler((TMessage)msg);

        lock (_asyncSubscribers)
        {
            if (!_asyncSubscribers.ContainsKey(messageType))
            {
                _asyncSubscribers[messageType] = new List<(string, Func<object, Task>)>();
            }

            if (!_asyncSubscribers[messageType].Any(s => s.SubscriberInfo == subscriberInfo))
            {
                _asyncSubscribers[messageType].Add((subscriberInfo, wrappedHandler));
            }
        }
    }

    public void Unsubscribe<TMessage>(Action<TMessage> handler) where TMessage : class
    {
        var messageType = typeof(TMessage);
        string subscriberInfo = GetSubscriberInfo();

        lock (_syncSubscribers)
        {
            if (_syncSubscribers.TryGetValue(messageType, out var subscribers))
            {
                subscribers.RemoveAll(s => s.SubscriberInfo == subscriberInfo);
            }
        }
    }

    public void Unsubscribe<TMessage>(Func<TMessage, Task> handler) where TMessage : class
    {
        var messageType = typeof(TMessage);
        string subscriberInfo = GetSubscriberInfo();

        lock (_asyncSubscribers)
        {
            if (_asyncSubscribers.TryGetValue(messageType, out var subscribers))
            {
                subscribers.RemoveAll(s => s.SubscriberInfo == subscriberInfo);
            }
        }
    }

    public bool TryGetSyncSubscribers(Type messageType, out List<(string SubscriberInfo, Action<object> Handler)>? subscribers)
    {
        lock (_syncSubscribers)
        {
            return _syncSubscribers.TryGetValue(messageType, out subscribers);
        }
    }

    public bool TryGetAsyncSubscribers(Type messageType, out List<(string SubscriberInfo, Func<object, Task> Handler)>? subscribers)
    {
        lock (_asyncSubscribers)
        {
            return _asyncSubscribers.TryGetValue(messageType, out subscribers);
        }
    }

    private string GetSubscriberInfo()
    {
        var stackFrame = new StackFrame(2, false);
        var method = stackFrame.GetMethod();
        string className = method?.DeclaringType?.Name ?? "UnknownClass";
        string methodName = method?.Name ?? "UnknownMethod";
        return $"{className}.{methodName}";
    }
}