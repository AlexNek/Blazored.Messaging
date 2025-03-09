namespace Blazor.Messaging;

public class SubscriberException
{
    public string SubscriberId { get; } // Could be class name or custom ID
    public Exception Exception { get; }

    public SubscriberException(string subscriberId, Exception exception)
    {
        SubscriberId = subscriberId;
        Exception = exception;
    }

    public override string ToString() => $"Error in {SubscriberId}: {Exception.Message}";
}