public class HandlerException : EventArgs
{
    public Exception Exception { get; }

    public Type MessageType { get; }

    public string SubscriberInfo { get; } // Format: "ClassName.FunctionName"

    public HandlerException(string subscriberInfo, Exception exception, Type messageType)
    {
        SubscriberInfo = subscriberInfo;
        Exception = exception;
        MessageType = messageType;
    }

    public override string ToString() =>
        $"Error in {SubscriberInfo} for {MessageType.Name}: {Exception.Message}";
}
