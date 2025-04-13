using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace Blazor.Messaging;

public class MessagingService : IMessagingService, IDisposable
{
    public event EventHandler<HandlerExceptionEventArgs>? HandlerExceptionOccurred
    {
        add => _exceptionHandlerManager.Event += value;
        remove => _exceptionHandlerManager.Event -= value;
    }

    private readonly Dictionary<Type, List<(string SubscriberInfo, Func<object, Task> Handler)>>
        _asyncSubscribers = new();

    private readonly UniqueEventHandlerManager<HandlerExceptionEventArgs> _exceptionHandlerManager = new();

    private readonly HandlerExecutor _handlerExecutor;

    private readonly TimeSpan _handlerTimeout;

    private readonly MessageProcessor _messageProcessor;

    private readonly ConcurrentQueue<(Type MsgType, object Msg, TaskCompletionSource<bool> Tcs)>
        _messageQueue = new();

    private readonly Task _processorTask;

    private readonly SynchronizationContext? _synchronizationContext;

    private readonly Dictionary<Type, List<(string SubscriberInfo, Action<object> Handler)>>
        _syncSubscribers = new();

    private bool _isRunning = true;

    public static TimeSpan AdditionalTimeoutDuration { get; } = TimeSpan.FromMilliseconds(100);

    public MessagingService(
        SynchronizationContext? synchronizationContext = null,
        TimeSpan? handlerTimeout = null)
    {
        Debug.WriteLine(
            $"MessagingService Constructor - SynchronizationContext.Current: {SynchronizationContext.Current}");
        _synchronizationContext = synchronizationContext ?? SynchronizationContext.Current;
        _handlerTimeout = handlerTimeout ?? TimeSpan.FromSeconds(5);

        if (_handlerTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(handlerTimeout),
                "Timeout cannot be negative.");
        }

        _handlerExecutor = new HandlerExecutor(
            _synchronizationContext,
            _handlerTimeout,
            OnHandlerException);

        _messageProcessor = new MessageProcessor(
            _messageQueue,
            _syncSubscribers,
            _asyncSubscribers,
            _handlerExecutor,
            _handlerTimeout,
            () => _isRunning,
            OnHandlerException); // Pass OnHandlerException as a delegate

        _processorTask = Task.Run(() => _messageProcessor.ProcessMessages());
    }

    public void Dispose()
    {
        _isRunning = false;
        _processorTask.Wait();
    }

    public Task Publish<TMessage>(TMessage message, bool throwOnTimeout = false)
        where TMessage : class
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        var tcs = new TaskCompletionSource<bool>();
        _messageQueue.Enqueue((typeof(TMessage), message, tcs));

        if (throwOnTimeout)
        {
            return Task.WhenAny(tcs.Task, Task.Delay(_handlerTimeout)).ContinueWith(
                t =>
                {
                    if (!tcs.Task.IsCompleted)
                    {
                        throw new TimeoutException(
                            $"Publishing {typeof(TMessage).Name} timed out after {_handlerTimeout.TotalMilliseconds}ms.");
                    }
                });
        }

        return tcs.Task;
    }

    public void Subscribe<TMessage>(Action<TMessage> handler)
        where TMessage : class
    {
        var messageType = typeof(TMessage);
        string subscriberInfo = GetMethodInfo(handler);
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
                Debug.WriteLine(
                    $"Subscribed sync handler for {messageType.Name} from {subscriberInfo}. Total: {_syncSubscribers[messageType].Count}");
            }
            else
            {
                Debug.WriteLine(
                    $"Duplicate sync subscription attempt for {messageType.Name} from {subscriberInfo}. Skipping.");
            }
        }
    }

    public void Subscribe<TMessage>(Func<TMessage, Task> handler)
        where TMessage : class
    {
        var messageType = typeof(TMessage);
        string subscriberInfo = GetMethodInfo(handler);
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
                Debug.WriteLine(
                    $"Subscribed async handler for {messageType.Name} from {subscriberInfo}. Total: {_asyncSubscribers[messageType].Count}");
            }
            else
            {
                Debug.WriteLine(
                    $"Duplicate async subscription attempt for {messageType.Name} from {subscriberInfo}. Skipping.");
            }
        }
    }

    public void Unsubscribe<TMessage>(Action<TMessage> handler)
        where TMessage : class
    {
        var messageType = typeof(TMessage);
        string subscriberInfo = GetMethodInfo(handler);

        lock (_syncSubscribers)
        {
            if (_syncSubscribers.TryGetValue(messageType, out var subscribers))
            {
                subscribers.RemoveAll(s => s.SubscriberInfo == subscriberInfo);
            }
        }
    }

    public void Unsubscribe<TMessage>(Func<TMessage, Task> handler)
        where TMessage : class
    {
        var messageType = typeof(TMessage);
        string subscriberInfo = GetMethodInfo(handler);

        lock (_asyncSubscribers)
        {
            if (_asyncSubscribers.TryGetValue(messageType, out var subscribers))
            {
                subscribers.RemoveAll(s => s.SubscriberInfo == subscriberInfo);
            }
        }
    }

    protected virtual void OnHandlerException(HandlerExceptionEventArgs e)
    {
        if (_synchronizationContext != null)
        {
            _synchronizationContext.Post(
                state =>
                {
                    _exceptionHandlerManager.Invoke(this, e);
                    if (!_exceptionHandlerManager.HasSubscribers)
                    {
                        Console.WriteLine(
                            $"Handler exception for {e.MessageType.Name}: {e.Exception.Message}");
                    }
                },
                null);
        }
        else
        {
            _exceptionHandlerManager.Invoke(this, e);
            if (!_exceptionHandlerManager.HasSubscribers)
            {
                Console.WriteLine(
                    $"Handler exception for {e.MessageType.Name}: {e.Exception.Message}");
            }
        }
    }

    //private static string GetMethodInfo(MethodInfo handlerMethod)
    //{
    //    return $"{handlerMethod.DeclaringType?.Name}::{handlerMethod.Name}";
    //}

    private static string GetMethodInfo(Delegate handler)
    {
        // Get method information
        var method = handler.Method;
        string className = method.DeclaringType?.Name ?? "UnknownClass";
        string methodName = method.Name;

        // Get instance information (if applicable)
        string instanceInfo = handler.Target != null
          ? $"InstanceID:{handler.Target.GetHashCode()}"
          : "StaticMethod";

        return $"{className}::{methodName} [{instanceInfo}]";
    }

    //private string GetSubscriberInfo()
    //  {
    //      var stackFrame = new StackFrame(2, false);
    //      var method = stackFrame.GetMethod();
    //      string className = method?.DeclaringType?.Name ?? "UnknownClass";
    //      string methodName = method?.Name ?? "UnknownMethod";
    //      return $"{className}.{methodName}";
    //  }

    private class HandlerTaskInfo
    {
        public string SubscriberId { get; set; } = string.Empty;

        public Task Task { get; set; } = Task.CompletedTask;
    }
}
