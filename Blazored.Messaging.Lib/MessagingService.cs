namespace Blazored.Messaging;

public class MessagingService : IMessagingService
{
  public event EventHandler<HandlerExceptionEventArgs> HandlerExceptionOccurred;
  private readonly Dictionary<Type, List<object>> _asyncSubscribers = new();
  private readonly Dictionary<Type, List<object>> _syncSubscribers = new();

  public async Task Publish<TMessage>(TMessage message) where TMessage : class
  {
    var messageType = typeof(TMessage);

    // Run sync handlers
    if (_syncSubscribers.ContainsKey(messageType))
    {
      var syncHandlers = _syncSubscribers[messageType].Cast<Action<TMessage>>().ToList();
      foreach (var handler in syncHandlers)
      {
        try
        {
          handler(message);
        }
        catch (Exception ex)
        {
          OnHandlerException(new HandlerExceptionEventArgs(ex, messageType, handler));
        }
      }
    }

    // Run async handlers
    if (_asyncSubscribers.ContainsKey(messageType))
    {
      var asyncHandlers = _asyncSubscribers[messageType].Cast<Func<TMessage, Task>>().ToList();
      foreach (var handler in asyncHandlers)
      {
        try
        {
          await handler(message);
        }
        catch (Exception ex)
        {
          OnHandlerException(new HandlerExceptionEventArgs(ex, messageType, handler));
        }
      }
    }
  }

  public void Subscribe<TMessage>(Action<TMessage> handler) where TMessage : class
  {
    var messageType = typeof(TMessage);
    if (!_syncSubscribers.ContainsKey(messageType))
    {
      _syncSubscribers[messageType] = new List<object>();
    }

    _syncSubscribers[messageType].Add(handler);
  }

  public void Subscribe<TMessage>(Func<TMessage, Task> handler) where TMessage : class
  {
    var messageType = typeof(TMessage);
    if (!_asyncSubscribers.ContainsKey(messageType))
    {
      _asyncSubscribers[messageType] = new List<object>();
    }

    _asyncSubscribers[messageType].Add(handler);
  }

  public void Unsubscribe<TMessage>(Action<TMessage> handler) where TMessage : class
  {
    var messageType = typeof(TMessage);
    if (_syncSubscribers.ContainsKey(messageType))
    {
      _syncSubscribers[messageType].Remove(handler);
    }
  }

  public void Unsubscribe<TMessage>(Func<TMessage, Task> handler) where TMessage : class
  {
    var messageType = typeof(TMessage);
    if (_asyncSubscribers.ContainsKey(messageType))
    {
      _asyncSubscribers[messageType].Remove(handler);
    }
  }

  protected virtual void OnHandlerException(HandlerExceptionEventArgs e)
  {
    // Raise the event; fallback to console if no subscribers
    HandlerExceptionOccurred?.Invoke(this, e);
    if (HandlerExceptionOccurred == null)
    {
      Console.WriteLine($"Handler exception for {e.MessageType.Name}: {e.Exception.Message}");
    }
  }
}