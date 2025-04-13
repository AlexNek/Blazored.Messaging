# Blazor.Messaging

### Overview
`Blazor.Messaging` is a simple, in-memory messaging tool made for Blazor apps. It uses a publish-subscribe (pub/sub) pattern. This means you can send and receive messages with clear types between parts of your app. You don’t need to connect components directly, which makes your code easier to manage.

I apologize for the confusion. You're right that this characteristic is common across libraries. Here's a concise "Motivation & Differentiation" section that addresses your points:

### Motivation & Differentiation

`Blazor.Messaging` was created to fill a gap in Blazor messaging solutions. While libraries like MediatR, Prism's EventAggregator, and custom event buses exist, they often introduce complexity or require extra setup unsuitable for Blazor's architecture. The goal was to develop a tool that:

1. Simplifies handling multiple message types within a single component.
2. Integrates seamlessly with Blazor's component lifecycle.
3. Minimizes boilerplate code and setup.

Unlike other solutions, `Blazor.Messaging`  is designed to be versatile and can be used across different frameworks, but it is particularly well-suited for Blazor applications, offering a straightforward pub/sub pattern that allows components to easily send and react to multiple message types without unnecessary complexity.

## Features
- **Synchronous and Asynchronous Messaging**: Supports both `Action<TMessage>` and `Func<TMessage, Task>` handlers.
- **Blazor-Friendly**: Works naturally with Blazor’s dependency injection and component lifecycle.
- **Type-Safe**: Uses generics for compile-time safety.
- **Exception Handling**: Optional `HandlerExceptionOccurred` event for managing handler errors.
- **Lightweight**: No external dependencies, pure in-memory implementation.
- **Duplicate Subscription Prevention**: Prevents duplicate subscriptions by the same subscriber.
- **Timeout Handling**: Configurable timeout for long-running subscriber tasks.


## Installation
Install via NuGet (once published) or reference locally:

```bash
dotnet add package Blazor.Messaging
```

## Setup in Blazor
1. **Register the Service**: Add `MessagingService` to your DI container in `Program.cs` (Blazor Server or WebAssembly):

```csharp
builder.Services.AddScoped<IMessagingService>(sp =>
{
    var synchronizationContext = SynchronizationContext.Current;
    return new MessagingService(synchronizationContext, TimeSpan.FromSeconds(10));
});

```

2. **Inject into Components**: Use `@inject` or constructor injection to access the service.

## Usage in Blazor

### Define a Message Type
Create a simple class for your message:

```csharp
public class UserLoggedInMessage
{
    public string Username { get; set; }
}
```

### Example: Blazor Component Communication

#### Publisher Component
Publishes a message when a user logs in:

```razor
@inject IMessagingService MessagingService

<h3>Login Component</h3>

<input @bind="username" placeholder="Enter username" />
<button @onclick="PublishLogin">Log In</button>

@code {
    private string username = string.Empty;

    private async Task PublishLogin()
    {
        var message = new UserLoggedInMessage { Username = username };
        await MessagingService.Publish(message);
        username = string.Empty;
    }
}
```

#### Subscriber Component
Subscribes to the message and updates the UI:

```razor
@inject IMessagingService MessagingService
@implements IDisposable

<h3>Notification Component</h3>

<p>@notification</p>

@code {
    private string notification = "Waiting for login...";

    private void SyncHandler(UserLoggedInMessage message)
    {
        notification = $"{message.Username} has logged in!";
        StateHasChanged();
    }

    protected override void OnInitialized()
    {
        MessagingService.Subscribe<UserLoggedInMessage>(SyncHandler);
    }

    public void Dispose()
    {
        MessagingService.Unsubscribe<UserLoggedInMessage>(SyncHandler);
    }
}
```

#### Async Subscriber Component
Handles the message asynchronously:

```razor
@inject IMessagingService MessagingService
@implements IDisposable

<h3>Async Notification Component</h3>

<p>@asyncNotification</p>

@code {
    private string asyncNotification = "Waiting for async response...";

    private async Task AsyncHandler(UserLoggedInMessage message)
    {
        await Task.Delay(1000);
        asyncNotification = $"{message.Username} logged in (async) after 1s!";
        StateHasChanged();
    }

    protected override void OnInitialized()
    {
        MessagingService.Subscribe<UserLoggedInMessage>(AsyncHandler);
    }

    public void Dispose()
    {
        MessagingService.Unsubscribe<UserLoggedInMessage>(AsyncHandler);
    }
}
```

### Handling Exceptions
Listen for handler exceptions:

```csharp
MessagingService.HandlerExceptionOccurred += (sender, e) =>
{
    Console.WriteLine($"Error in {e.MessageType.Name} handler: {e.Exception.Message}");
};
```

builder.Services.AddScoped<IMessagingService>(sp =>
                {
                    var synchronizationContext = SynchronizationContext.Current;

                    return new MessagingService(synchronizationContext, TimeSpan.FromSeconds(10));
                });


### Handling Timeouts
Occasionally, subscribers may take an extended period to complete. You can establish a global timeout for all subscribers by configuring a `MessagingService`.
In certain scenarios, such as handler debugging, all handlers may not execute if additional threads are not used.
If you encounter a timeout message with a duration exceeding the combined value of the global timeout and `MessagingService.AdditionalTimeoutDuration`, it indicates that the timeout was triggered by the global handler.

## API Reference

### `IMessagingService`
- **`Subscribe<TMessage>(Action<TMessage> handler)`**: Synchronous subscription.
- **`Subscribe<TMessage>(Func<TMessage, Task> handler)`**: Asynchronous subscription.
- **`Publish<TMessage>(TMessage message)`**: Publishes a message to all subscribers.
- **`Unsubscribe<TMessage>(Action<TMessage> handler)`**: Removes a synchronous subscription.
- **`Unsubscribe<TMessage>(Func<TMessage, Task> handler)`**: Removes an asynchronous subscription.
- **`event HandlerExceptionOccurred`**: Fired when a handler throws an exception.

### `MessagingService`
- Stores subscribers in memory using dictionaries.
- Executes handlers sequentially.
- Logs unhandled exceptions to the console by default.
- Prevents duplicate subscriptions.
- Configurable timeout for long-running subscriber tasks.

## Notes
- **Lifecycle Management**: Unsubscribe in `Dispose` to avoid memory leaks in Blazor components.
- **Scope**: In-memory only, ideal for single-user apps (e.g., Blazor WebAssembly) or scoped Server apps.

## Contributing
Contributions welcome! Submit issues or PRs to improve the library.

## License
Licensed under the **Apache License 2.0** - see [LICENSE](LICENSE) for details.

## Version History
- **v1.0.0**: Initial release.
- **v1.1.0**: Added prevention of duplicate subscriptions and long running task timeout.
- **v1.2.0**: Missed file - SubscriberException.
- **v1.2.1**: Downgrade to .NET 8.0.
- **v1.3.0**: Improvements for breakpoint debugging on subscription handler.
- **v1.3.1**: Rename back HandlerExceptionEventArgs.
- **v1.4.0**: Updated subscription mechanism to include instance ID, allowing the same class to be used in different places without conflicts.
