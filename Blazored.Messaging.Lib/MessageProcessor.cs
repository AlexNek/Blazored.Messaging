namespace Blazor.Messaging;

internal class MessageProcessor
{
    private readonly MessageQueue _messageQueue;
    private readonly MessageSubscriberManager _subscriberManager;
    private readonly ExceptionHandlerManager _exceptionHandlerManager;
    private readonly SynchronizationContext? _synchronizationContext;

    public TimeSpan HandlerTimeout { get; }

    public MessageProcessor(
        MessageQueue messageQueue,
        MessageSubscriberManager subscriberManager,
        ExceptionHandlerManager exceptionHandlerManager,
        TimeSpan handlerTimeout,
        SynchronizationContext? synchronizationContext = null)
    {
        _messageQueue = messageQueue;
        _subscriberManager = subscriberManager;
        _exceptionHandlerManager = exceptionHandlerManager;
        HandlerTimeout = handlerTimeout;
        _synchronizationContext = synchronizationContext;
    }

    public async Task ProcessMessages(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested || !_messageQueue.IsEmpty)
        {
            if (_messageQueue.TryDequeue(out var item))
            {
                var (msgType, message, tcs) = item;
                var taskInfos = new List<HandlerTaskInfo>();

                if (_subscriberManager.TryGetSyncSubscribers(msgType, out var syncSubscribers))
                {
                    foreach (var (subscriberId, handler) in syncSubscribers)
                    {
                        taskInfos.Add(new HandlerTaskInfo
                                          {
                                              Task = ExecuteSyncHandler(handler, message, subscriberId, msgType, cancellationToken),
                                              SubscriberId = subscriberId
                                          });
                    }
                }

                if (_subscriberManager.TryGetAsyncSubscribers(msgType, out var asyncSubscribers))
                {
                    foreach (var (subscriberId, handler) in asyncSubscribers)
                    {
                        taskInfos.Add(new HandlerTaskInfo
                                          {
                                              Task = ExecuteAsyncHandler(handler, message, subscriberId, msgType, cancellationToken),
                                              SubscriberId = subscriberId
                                          });
                    }
                }

                try
                {
                    await RunWithTimeout(taskInfos, HandlerTimeout, msgType, cancellationToken);
                    tcs.SetResult(true);
                }
                catch (TimeoutException)
                {
                    tcs.SetResult(false);
                }
                catch (Exception)
                {
                    tcs.SetResult(false);
                }
            }
            else
            {
                await Task.Delay(10, cancellationToken);
            }
        }
    }

    private async Task ExecuteSyncHandler(Action<object> handler, object message, string subscriberId, Type messageType, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(HandlerTimeout);
        var tcs = new TaskCompletionSource<bool>();

        try
        {
            if (_synchronizationContext != null)
            {
                _synchronizationContext.Post(_ =>
                    {
                        handler(message);
                        tcs.SetResult(true);
                    }, null);
            }
            else
            {
                handler(message);
                tcs.SetResult(true);
            }

            await tcs.Task;
        }
        catch (Exception ex)
        {
            _exceptionHandlerManager.OnHandlerException(new HandlerExceptionEventArgs(subscriberId, ex, messageType));
            tcs.SetResult(false);
        }
    }

    private async Task ExecuteAsyncHandler(Func<object, Task> handler, object message, string subscriberId, Type messageType, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(HandlerTimeout);
        var tcs = new TaskCompletionSource<bool>();

        try
        {
            if (_synchronizationContext != null)
            {
                _synchronizationContext.Post(async _ =>
                    {
                        await handler(message);
                        tcs.SetResult(true);
                    }, null);
            }
            else
            {
                await handler(message);
                tcs.SetResult(true);
            }

            await tcs.Task;
        }
        catch (Exception ex)
        {
            _exceptionHandlerManager.OnHandlerException(new HandlerExceptionEventArgs(subscriberId, ex, messageType));
            tcs.SetResult(false);
        }
    }

    private async Task RunWithTimeout(List<HandlerTaskInfo> taskInfos, TimeSpan timeout, Type messageType, CancellationToken cancellationToken)
    {
        var tasks = taskInfos.Select(t => t.Task).ToArray();
        var timeoutTask = Task.Delay(timeout, cancellationToken);

        var completedTask = await Task.WhenAny(Task.WhenAll(tasks), timeoutTask);

        if (completedTask == timeoutTask)
        {
            foreach (var taskInfo in taskInfos.Where(t => !t.Task.IsCompleted))
            {
                _exceptionHandlerManager.OnHandlerException(new HandlerExceptionEventArgs(
                    taskInfo.SubscriberId,
                    new TimeoutException($"Handler timed out after {timeout.TotalMilliseconds}ms"),
                    messageType));
            }

            throw new TimeoutException($"Handlers timed out after {timeout.TotalMilliseconds}ms");
        }

        await Task.WhenAll(tasks);
    }

    private class HandlerTaskInfo
    {
        public string SubscriberId { get; set; }
        public Task Task { get; set; }
    }
}