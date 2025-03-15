using FluentAssertions;

namespace Blazor.Messaging.Tests;

public class MessagingServiceTests : IDisposable
{
    private readonly MessagingService _messagingService;

    public MessagingServiceTests()
    {
        // Use null SynchronizationContext (Blazor WASM-like) for deterministic testing
        _messagingService = new MessagingService(null, TimeSpan.FromSeconds(1));
    }

    public void Dispose()
    {
        _messagingService.Dispose();
    }

    [Fact]
    public async Task Publish_AsyncHandlerThrowsException_ShouldRaiseExceptionEvent()
    {
        // Arrange
        var exceptionMessage = "Async handler failed";
        var receivedException = (Exception?)null;
        _messagingService.HandlerExceptionOccurred +=
            (_, args) => receivedException = args.Exception;

        _messagingService.Subscribe<string>(
            async _ =>
                {
                    await Task.Yield(); // Simulate async work
                    throw new InvalidOperationException(exceptionMessage);
                });

        // Act
        await _messagingService.Publish("test message");

        // Assert
        receivedException.Should().NotBeNull("an exception should have been raised");
        receivedException.Should().BeOfType<InvalidOperationException>();
        receivedException!.Message.Should().Be(exceptionMessage);
    }

    [Fact]
    public async Task Publish_AsyncHandlerTimesOut_ShouldRaiseTimeoutExceptionEvent()
    {
        // Arrange
        var receivedException = (Exception?)null;
        _messagingService.HandlerExceptionOccurred +=
            (_, args) => receivedException = args.Exception;

        _messagingService.Subscribe<string>(
            async _ =>
                {
                    await Task.Delay(1500); // Exceed 1-second timeout
                });

        // Act
        await _messagingService.Publish("test message");

        // Assert
        receivedException.Should().NotBeNull("a timeout exception should have been raised");
        receivedException.Should().BeOfType<TimeoutException>();
        receivedException!.Message.Should().Contain("Async handler timed out after 1000ms");
    }

    [Fact]
    public async Task Publish_SyncHandlerThrowsException_ShouldRaiseExceptionEvent()
    {
        // Arrange
        var exceptionMessage = "Sync handler failed";
        var receivedException = (Exception?)null;
        _messagingService.HandlerExceptionOccurred +=
            (_, args) => receivedException = args.Exception;

        _messagingService.Subscribe<string>(
            _ => throw new InvalidOperationException(exceptionMessage));

        // Act
        await _messagingService.Publish("test message");

        // Assert
        receivedException.Should().NotBeNull("an exception should have been raised");
        receivedException.Should().BeOfType<InvalidOperationException>();
        receivedException!.Message.Should().Be(exceptionMessage);
    }

    [Fact]
    public async Task Publish_SyncHandlerTimesOut_ShouldRaiseTimeoutExceptionEvent()
    {
        // Arrange
        var receivedException = (Exception?)null;
        _messagingService.HandlerExceptionOccurred +=
            (_, args) => receivedException = args.Exception;

        _messagingService.Subscribe<string>(
            _ =>
                {
                    Thread.Sleep(1500); // Exceed 1-second timeout
                });

        // Act
        await _messagingService.Publish("test message");

        // Assert
        receivedException.Should().NotBeNull("a timeout exception should have been raised");
        receivedException.Should().BeOfType<TimeoutException>();
        receivedException!.Message.Should().Contain("Sync handler timed out after 1000ms");
    }

    [Fact]
    public async Task Publish_ValidAsyncHandler_ShouldCompleteSuccessfully()
    {
        // Arrange
        var handlerCalled = false;
        _messagingService.Subscribe<string>(
            async _ =>
                {
                    await Task.Yield();
                    handlerCalled = true;
                });

        // Act
        var task = _messagingService.Publish("test message");
        await task;

        // Assert
        task.IsCompletedSuccessfully.Should()
            .BeTrue("the publish task should complete successfully");
        handlerCalled.Should().BeTrue("the handler should have been called");
    }

    [Fact]
    public async Task Publish_ValidSyncHandler_ShouldCompleteSuccessfully()
    {
        // Arrange
        var handlerCalled = false;
        _messagingService.Subscribe<string>(_ => handlerCalled = true);

        // Act
        var task = _messagingService.Publish("test message");
        await task;

        // Assert
        task.IsCompletedSuccessfully.Should()
            .BeTrue("the publish task should complete successfully");
        handlerCalled.Should().BeTrue("the handler should have been called");
    }
}
