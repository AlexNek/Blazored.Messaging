using System.Collections.Concurrent;

namespace Blazor.Messaging;

public class MessageProcessor
{
    private readonly ConcurrentQueue<(Type MsgType, object Msg, TaskCompletionSource<bool> Tcs)> _messageQueue;
    private readonly Dictionary<Type, List<(string SubscriberInfo, Action<object> Handler)>> _syncSubscribers;
    private readonly Dictionary<Type, List<(string SubscriberInfo, Func<object, Task> Handler)>> _asyncSubscribers;
    private readonly HandlerExecutor _handlerExecutor;
    private readonly TimeSpan _handlerTimeout;
    private readonly Func<bool> _isRunning;
    private readonly Action<HandlerExceptionEventArgs> _onHandlerException; // Delegate to call OnHandlerException

    public MessageProcessor(
        ConcurrentQueue<(Type MsgType, object Msg, TaskCompletionSource<bool> Tcs)> messageQueue,
        Dictionary<Type, List<(string SubscriberInfo, Action<object> Handler)>> syncSubscribers,
        Dictionary<Type, List<(string SubscriberInfo, Func<object, Task> Handler)>> asyncSubscribers,
        HandlerExecutor handlerExecutor,
        TimeSpan handlerTimeout,
        Func<bool> isRunning,
        Action<HandlerExceptionEventArgs> onHandlerException) // Added to access OnHandlerException
    {
        _messageQueue = messageQueue;
        _syncSubscribers = syncSubscribers;
        _asyncSubscribers = asyncSubscribers;
        _handlerExecutor = handlerExecutor;
        _handlerTimeout = handlerTimeout;
        _isRunning = isRunning;
        _onHandlerException = onHandlerException;
    }

    public async Task ProcessMessages()
    {
        while (_isRunning() || !_messageQueue.IsEmpty)
        {
            if (_messageQueue.TryDequeue(out var item))
            {
                var (msgType, message, tcs) = item;
                var taskInfos = new List<HandlerTaskInfo>();

                if (_syncSubscribers.TryGetValue(msgType, out var syncSubscribers))
                {
                    var syncList = syncSubscribers.ToList();
                    foreach (var (subscriberId, handler) in syncList)
                    {
                        taskInfos.Add(
                            new HandlerTaskInfo
                            {
                                Task = _handlerExecutor.ExecuteSyncHandler(handler, message, subscriberId, msgType),
                                SubscriberId = subscriberId
                            });
                    }
                }

                if (_asyncSubscribers.TryGetValue(msgType, out var asyncSubscribers))
                {
                    var asyncList = asyncSubscribers.ToList();
                    foreach (var (subscriberId, handler) in asyncList)
                    {
                        taskInfos.Add(
                            new HandlerTaskInfo
                            {
                                Task = _handlerExecutor.ExecuteAsyncHandler(handler, message, subscriberId, msgType),
                                SubscriberId = subscriberId
                            });
                    }
                }

                try
                {
                    await RunWithTimeout(taskInfos, _handlerTimeout, msgType);
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
                await Task.Delay(10);
            }
        }
    }

    private async Task RunWithTimeout(List<HandlerTaskInfo> taskInfos, TimeSpan timeout, Type messageType)
    {
        var tasks = taskInfos.Select(t => t.Task).ToArray();

        // it is fallback only for main timeout handler, so give main timeouts time for execution
        TimeSpan newTimeout = timeout + MessagingService.AdditionalTimeoutDuration;
        var timeoutTask = Task.Delay(newTimeout);
        var completedTask = await Task.WhenAny(Task.WhenAll(tasks), timeoutTask);

        if (completedTask == timeoutTask)
        {
            // Timeout occurred
            var unfinishedTasks = taskInfos.Where(t => !t.Task.IsCompleted).ToList();
            foreach (var taskInfo in unfinishedTasks)
            {
                _onHandlerException(
                    new HandlerExceptionEventArgs(
                        taskInfo.SubscriberId,
                        new TimeoutException($"Handler for {messageType} timed out after {newTimeout.TotalMilliseconds}ms"),
                        messageType));
            }

            throw new TimeoutException($"Handler timed out after {newTimeout.TotalMilliseconds}ms");
        }

        await Task.WhenAll(tasks); // Ensure exceptions are propagated.
    }

    private class HandlerTaskInfo
    {
        public string SubscriberId { get; set; } = string.Empty;
        public Task Task { get; set; } = Task.CompletedTask;
    }
}