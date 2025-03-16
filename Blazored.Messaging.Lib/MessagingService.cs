using System.Diagnostics;

namespace Blazor.Messaging;

public class MessagingService : IMessagingService, IDisposable
{
    public event EventHandler<HandlerExceptionEventArgs> HandlerExceptionOccurred
    {
        add => _exceptionHandlerManager.HandlerExceptionOccurred += value;
        remove => _exceptionHandlerManager.HandlerExceptionOccurred -= value;
    }

    private readonly CancellationTokenSource _cts = new();

    private readonly ExceptionHandlerManager _exceptionHandlerManager = new();

    private readonly MessageProcessor _messageProcessor;

    private readonly MessageQueue _messageQueue = new();

    private readonly Task _processorTask;

    private readonly MessageSubscriberManager _subscriberManager = new();

    public MessagingService(
        SynchronizationContext? synchronizationContext = null,
        TimeSpan? handlerTimeout = null)
    {
        Debug.WriteLine(
            $"MessagingService Constructor - SynchronizationContext.Current: {SynchronizationContext.Current}");
        var timeout = handlerTimeout ?? TimeSpan.FromSeconds(5);

        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(handlerTimeout),
                "Timeout cannot be negative.");
        }

        _messageProcessor = new MessageProcessor(
            _messageQueue,
            _subscriberManager,
            _exceptionHandlerManager,
            timeout,
            synchronizationContext);

        _processorTask = _messageProcessor.ProcessMessages(_cts.Token);
    }

    public void Dispose()
    {
        // Cancel the task
        _cts.Cancel();

        try
        {
            // Wait for the task to complete gracefully
            _processorTask.Wait();
        }
        catch (AggregateException ex)
        {
            // Handle TaskCanceledException gracefully
            if (ex.InnerException is not TaskCanceledException)
            {
                // Rethrow if it's not a TaskCanceledException
                throw;
            }
        }
        finally
        {
            // Ensure the CancellationTokenSource is disposed
            _cts.Dispose();
        }
    }

    public Task Publish<TMessage>(TMessage message, bool throwOnTimeout = false)
        where TMessage : class
    {
        var tcs = new TaskCompletionSource<bool>();
        _messageQueue.Enqueue(typeof(TMessage), message, tcs);

        if (throwOnTimeout)
        {
            return Task.WhenAny(tcs.Task, Task.Delay(_messageProcessor.HandlerTimeout))
                .ContinueWith(
                    t =>
                        {
                            if (!tcs.Task.IsCompleted)
                            {
                                throw new TimeoutException(
                                    $"Publishing {typeof(TMessage).Name} timed out.");
                            }
                        });
        }

        return tcs.Task;
    }

    public void Subscribe<TMessage>(Action<TMessage> handler)
        where TMessage : class
    {
        _subscriberManager.Subscribe(handler);
    }

    public void Subscribe<TMessage>(Func<TMessage, Task> handler)
        where TMessage : class
    {
        _subscriberManager.Subscribe(handler);
    }

    public void Unsubscribe<TMessage>(Action<TMessage> handler)
        where TMessage : class
    {
        _subscriberManager.Unsubscribe(handler);
    }

    public void Unsubscribe<TMessage>(Func<TMessage, Task> handler)
        where TMessage : class
    {
        _subscriberManager.Unsubscribe(handler);
    }
}
