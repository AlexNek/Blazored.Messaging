using System.Collections.Concurrent;
using System.Diagnostics;

namespace Blazor.Messaging;

public class UniqueEventHandlerManager<T> where T : EventArgs
{
    private readonly HashSet<int> _handlerIds = new();
    private EventHandler<T>? _eventHandler;

    public event EventHandler<T>? Event
    {
        add
        {
            if (value != null)
            {
                lock (_handlerIds)
                {
                    int handlerId = value.GetHashCode();
                    if (_handlerIds.Add(handlerId))
                    {
                        _eventHandler += value;
                        Debug.WriteLine($"Added handler ID {handlerId}. Total handlers: {_handlerIds.Count}");
                    }
                    else
                    {
                        Debug.WriteLine($"Handler ID {handlerId} already exists. Skipping duplicate.");
                    }
                }
            }
        }
        remove
        {
            if (value != null)
            {
                lock (_handlerIds)
                {
                    int handlerId = value.GetHashCode();
                    if (_handlerIds.Remove(handlerId))
                    {
                        _eventHandler -= value;
                        Debug.WriteLine($"Removed handler ID {handlerId}. Total handlers: {_handlerIds.Count}");
                    }
                }
            }
        }
    }

    // Method to invoke the event
    public void Invoke(object? sender, T args)
    {
        _eventHandler?.Invoke(sender, args);
    }

    // Property to check if there are any subscribers
    public bool HasSubscribers => _eventHandler != null;
}
