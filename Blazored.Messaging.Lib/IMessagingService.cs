namespace Blazor.Messaging;

public interface IMessagingService
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
    event EventHandler<HandlerExceptionEventArgs> HandlerExceptionOccurred;


    /// <summary>
    /// Publishes the specified message to the message queue for processing by a consumer.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message being published.</typeparam>
    /// <param name="message">The message to be published. Must not be <c>null</c>.</param>
    /// <param name="throwOnTimeout">
    /// If set to <c>true</c>, a <see cref="TimeoutException"/> will be thrown if the message is not processed 
    /// within the configured timeout period. If set to <c>false</c>, the method will return a task that may 
    /// remain incomplete if the timeout occurs.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> that represents the asynchronous operation. The task completes when the message 
    /// is successfully processed by a consumer or, if <paramref name="throwOnTimeout"/> is set to <c>false</c>, 
    /// it may remain incomplete in case of a timeout.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if the <paramref name="message"/> parameter is <c>null</c>.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Thrown if <paramref name="throwOnTimeout"/> is set to <c>true</c> and the message is not processed 
    /// within the configured timeout period.
    /// </exception>
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
