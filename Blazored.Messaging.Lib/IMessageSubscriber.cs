namespace Blazor.Messaging;

public interface IMessageSubscriber
{
    void Subscribe<TMessage>(Action<TMessage> handler) where TMessage : class;
    void Subscribe<TMessage>(Func<TMessage, Task> handler) where TMessage : class;
    void Unsubscribe<TMessage>(Action<TMessage> handler) where TMessage : class;
    void Unsubscribe<TMessage>(Func<TMessage, Task> handler) where TMessage : class;
}