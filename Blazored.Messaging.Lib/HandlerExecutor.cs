using System.Diagnostics;

namespace Blazor.Messaging;

public class HandlerExecutor
{
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly TimeSpan _handlerTimeout;
    private readonly Action<HandlerExceptionEventArgs> _exceptionHandler;

    public HandlerExecutor(
        SynchronizationContext? synchronizationContext,
        TimeSpan handlerTimeout,
        Action<HandlerExceptionEventArgs> exceptionHandler)
    {
        _synchronizationContext = synchronizationContext;
        _handlerTimeout = handlerTimeout;
        _exceptionHandler = exceptionHandler;
    }

    public async Task ExecuteAsyncHandler(
        Func<object, Task> handler,
        object message,
        string subscriberInfo,
        Type messageType)
    {
        using var cts = new CancellationTokenSource(_handlerTimeout);
        var tcs = new TaskCompletionSource<bool>();
        var token = cts.Token;

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
                                   _exceptionHandler(
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
            _exceptionHandler(new HandlerExceptionEventArgs(subscriberInfo, ex, messageType));
        }
    }

    public async Task ExecuteSyncHandler(
        Action<object> handler,
        object message,
        string subscriberInfo,
        Type messageType)
    {
        using var cts = new CancellationTokenSource(_handlerTimeout);
        var tcs = new TaskCompletionSource<bool>();
        var token = cts.Token;

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
                    tcs);
            }

            using (cts.Token.Register(
                       () =>
                           {
                               if (!tcs.Task.IsCompleted)
                               {
                                   _exceptionHandler(
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
            _exceptionHandler(new HandlerExceptionEventArgs(subscriberInfo, ex, messageType));
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
            _exceptionHandler(new HandlerExceptionEventArgs(subscriberInfo, ex, messageType));
            tcs.TrySetResult(false);
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
            _exceptionHandler(new HandlerExceptionEventArgs(subscriberInfo, ex, messageType));
            tcs.TrySetResult(false);
        }
    }
}