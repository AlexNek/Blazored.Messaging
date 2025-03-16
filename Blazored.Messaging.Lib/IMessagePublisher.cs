namespace Blazor.Messaging;

public interface IMessagePublisher
{
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

    Task Publish<TMessage>(TMessage message, bool throwOnTimeout = false) where TMessage : class;
}