namespace Blazor.Messaging;

public interface IMessagingService : IMessagePublisher, IMessageSubscriber
{
    /// <summary>
    /// Occurs when an exception is thrown during message handling by a consumer.
    /// </summary>
    /// <remarks>
    /// This event provides a mechanism for external code to be notified of exceptions that occur
    /// during the processing of messages by handlers. It allows for centralized exception logging,
    /// monitoring, or custom error handling strategies without modifying the core message processing logic.
    /// </remarks>
    /// <example>
    /// <code>
    /// messagePublisher.HandlerExceptionOccurred += (sender, args) =>
    /// {
    ///     Logger.LogError(args.Exception, "Error occurred while handling message of type {MessageType}", args.MessageType);
    /// };
    /// </code>
    /// </example>
    event EventHandler<HandlerExceptionEventArgs>? HandlerExceptionOccurred;
}
