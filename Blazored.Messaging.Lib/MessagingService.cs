using System.Collections.Concurrent;
using System.Diagnostics;

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

    /// <summary>
    /// Use HashSet to track unique subscribers by their hash code
    /// </summary>
    private readonly UniqueEventHandlerManager<HandlerExceptionEventArgs> _exceptionHandlerManager =
        new();

    private readonly TimeSpan _handlerTimeout;

    private readonly ConcurrentQueue<(Type MsgType, object Msg, TaskCompletionSource<bool> Tcs)>
        _messageQueue = new();

    private readonly Task _processorTask;

    private readonly SynchronizationContext? _synchronizationContext;

    private readonly Dictionary<Type, List<(string SubscriberInfo, Action<object> Handler)>>
        _syncSubscribers = new();

    private bool _isRunning = true;

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

        _processorTask = Task.Run(ProcessMessages);
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

        // Create a TaskCompletionSource to represent the completion of the message handling.
        // This task will be completed by the consumer that processes the message.
        var tcs = new TaskCompletionSource<bool>();

        // Enqueue the message into the message queue for processing.
        // The tuple contains:
        // - The type of the message (for identifying the message type),
        // - The actual message object,
        // - The TaskCompletionSource that will be completed when processing is done.
        _messageQueue.Enqueue((typeof(TMessage), message, tcs));

        if (throwOnTimeout)
        {
            // Use Task.WhenAny to wait for either:
            // - The tcs.Task to complete (indicating successful processing of the message),
            // - Or a Task.Delay to complete (indicating a timeout has occurred).
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

        // If throwOnTimeout is false, simply return the TaskCompletionSource's task.
        // The caller can await this task and handle it as needed.
        return tcs.Task;
    }

    public void Subscribe<TMessage>(Action<TMessage> handler)
        where TMessage : class
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

            // Check for existing subscription by SubscriberInfo
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
        string subscriberInfo = GetSubscriberInfo();
        Func<object, Task> wrappedHandler = msg => handler((TMessage)msg);

        lock (_asyncSubscribers)
        {
            if (!_asyncSubscribers.ContainsKey(messageType))
            {
                _asyncSubscribers[messageType] = new List<(string, Func<object, Task>)>();
            }

            // Check for existing subscription by SubscriberInfo
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
        string subscriberInfo = GetSubscriberInfo();

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
        string subscriberInfo = GetSubscriberInfo();

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

    private async Task ExecuteAsyncCallback(
        Func<object, Task> handler,
        object message,
        string subscriberInfo,
        Type messageType,
        CancellationToken token,
        TaskCompletionSource<bool> tcs)
    {
        Debug.WriteLine(
            $"ExecuteAsyncHandler Callback - Thread ID: {Thread.CurrentThread.ManagedThreadId}");
        if (token.IsCancellationRequested)
        {
            tcs.TrySetCanceled();
            return;
        }

        try
        {
            await handler(message);
            tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
            OnHandlerException(new HandlerExceptionEventArgs(subscriberInfo, ex, messageType));
            tcs.TrySetResult(false);
        }
    }

    private async Task ExecuteAsyncHandler(
        Func<object, Task> handler,
        object message,
        string subscriberInfo,
        Type messageType)
    {
        using var cts = new CancellationTokenSource(_handlerTimeout);
        var tcs = new TaskCompletionSource<bool>();
        var token = cts.Token; // Capture the token before disposal

        try
        {
            if (_synchronizationContext != null)
            {
                _synchronizationContext.Post(
                    _ => ExecuteAsyncCallback(
                        handler,
                        message,
                        subscriberInfo,
                        messageType,
                        token,
                        tcs),
                    null);
            }
            else
            {
                // Blazor WASM
                await ExecuteAsyncCallback(
                    handler,
                    message,
                    subscriberInfo,
                    messageType,
                    token,
                    tcs); 
            }

            using (cts.Token.Register(
                       () =>
                           {
                               if (!tcs.Task.IsCompleted)
                               {
                                   OnHandlerException(
                                       new HandlerExceptionEventArgs(
                                           subscriberInfo,
                                           new TimeoutException(
                                               $"Async handler timed out after {_handlerTimeout.TotalMilliseconds}ms"),
                                           messageType));
                                   tcs.TrySetResult(false);
                               }
                           }))
            {
                await tcs.Task;
            }
        }
        catch (Exception ex)
        {
            OnHandlerException(new HandlerExceptionEventArgs(subscriberInfo, ex, messageType));
        }
    }

    private void ExecuteSyncCallback(
        Action<object> handler,
        object message,
        string subscriberInfo,
        Type messageType,
        CancellationToken token,
        TaskCompletionSource<bool> tcs)
    {
        Debug.WriteLine(
            $"ExecuteSyncHandler Callback - Thread ID: {Thread.CurrentThread.ManagedThreadId}");
        if (token.IsCancellationRequested)
        {
            tcs.TrySetCanceled();
            return;
        }

        try
        {
            handler(message);
            tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
            OnHandlerException(new HandlerExceptionEventArgs(subscriberInfo, ex, messageType));
            tcs.TrySetResult(false);
        }
    }

    private async Task ExecuteSyncHandler(
        Action<object> handler,
        object message,
        string subscriberInfo,
        Type messageType)
    {
        using var cts = new CancellationTokenSource(_handlerTimeout);
        var tcs = new TaskCompletionSource<bool>();
        var token = cts.Token; // Capture the token before disposal

        try
        {
            if (_synchronizationContext != null)
            {
                _synchronizationContext.Post(
                    _ => ExecuteSyncCallback(
                        handler,
                        message,
                        subscriberInfo,
                        messageType,
                        token,
                        tcs),
                    null);
            }
            else
            {
                ExecuteSyncCallback(
                    handler,
                    message,
                    subscriberInfo,
                    messageType,
                    token,
                    tcs); // Blazor WASM
            }

            using (cts.Token.Register(
                       () =>
                           {
                               if (!tcs.Task.IsCompleted)
                               {
                                   OnHandlerException(
                                       new HandlerExceptionEventArgs(
                                           subscriberInfo,
                                           new TimeoutException(
                                               $"Sync handler timed out after {_handlerTimeout.TotalMilliseconds}ms"),
                                           messageType));
                                   tcs.TrySetResult(false);
                               }
                           }))
            {
                await tcs.Task;
            }
        }
        catch (Exception ex)
        {
            OnHandlerException(new HandlerExceptionEventArgs(subscriberInfo, ex, messageType));
        }
    }

    private string GetSubscriberInfo()
    {
        var stackFrame = new StackFrame(
            2,
            false); // Skip 2 frames: this method and Subscribe/Unsubscribe
        var method = stackFrame.GetMethod();
        string className = method?.DeclaringType?.Name ?? "UnknownClass";
        string methodName = method?.Name ?? "UnknownMethod";
        return $"{className}.{methodName}";
    }

    private async Task ProcessMessages()
    {
        while (_isRunning || !_messageQueue.IsEmpty)
        {
            if (_messageQueue.TryDequeue(out var item))
            {
                var (msgType, message, tcs) = item;
                var tasks = new List<Task>();

                if (_syncSubscribers.TryGetValue(msgType, out var syncSubscribers))
                {
                    var syncList = syncSubscribers.ToList();
                    foreach (var (subscriberInfo, handler) in syncList)
                    {
                        tasks.Add(ExecuteSyncHandler(handler, message, subscriberInfo, msgType));
                    }
                }

                if (_asyncSubscribers.TryGetValue(msgType, out var asyncSubscribers))
                {
                    var asyncList = asyncSubscribers.ToList();
                    foreach (var (subscriberInfo, handler) in asyncList)
                    {
                        tasks.Add(ExecuteAsyncHandler(handler, message, subscriberInfo, msgType));
                    }
                }

                await Task.WhenAll(tasks);
                tcs.SetResult(true);
            }
            else
            {
                await Task.Delay(10);
            }
        }
    }
}
