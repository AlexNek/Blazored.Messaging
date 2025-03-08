namespace Blazored.Messaging;

public interface IMessagingService
{
  void Subscribe<TMessage>(Action<TMessage> handler) where TMessage : class;
  void Subscribe<TMessage>(Func<TMessage, Task> handler) where TMessage : class;
  Task Publish<TMessage>(TMessage message) where TMessage : class;
  void Unsubscribe<TMessage>(Action<TMessage> handler) where TMessage : class;
  void Unsubscribe<TMessage>(Func<TMessage, Task> handler) where TMessage : class;

  // Optional: Event to notify about handler exceptions
  event EventHandler<HandlerExceptionEventArgs> HandlerExceptionOccurred;
}