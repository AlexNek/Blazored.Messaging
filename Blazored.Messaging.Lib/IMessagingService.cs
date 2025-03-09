namespace Blazor.Messaging;

public interface IMessagingService
{
    // Optional: Event to notify about handler exceptions
    event EventHandler<HandlerExceptionEventArgs> HandlerExceptionOccurred;

    Task Publish<TMessage>(TMessage message, bool throwOnTimeout = false)
        where TMessage : class;

    void Subscribe<TMessage>(Action<TMessage> handler)
        where TMessage : class;

    void Subscribe<TMessage>(Func<TMessage, Task> handler)
        where TMessage : class;

    void Unsubscribe<TMessage>(Action<TMessage> handler)
        where TMessage : class;

    void Unsubscribe<TMessage>(Func<TMessage, Task> handler)
        where TMessage : class;
}
