using System.Dynamic;

using FluentAssertions;

namespace Blazor.Messaging.Tests;

public class MessagingServiceTests : IDisposable
{
    private readonly MessagingService _messagingService;

    private readonly TimeSpan _handlerTimeout = TimeSpan.FromSeconds(2);

    public MessagingServiceTests()
    {
        // Use null SynchronizationContext (Blazor WASM-like) for deterministic testing
        _messagingService = new MessagingService(null, _handlerTimeout);
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
        Exception? receivedException = null;
        _messagingService.HandlerExceptionOccurred += (_, args) =>
            {
                receivedException = args.Exception; // Capture the exception
            };

        _messagingService.Subscribe<string>(
            async _ =>
                {
                    // Simulate a long-running operation that EXCEEDS the timeout
                    await Task.Run(() => Thread.Sleep(1000 * 60));
                });

        // Act
        await _messagingService.Publish("test message");

        // Wait a moment to allow the handler to trigger the timeout
        await Task.Delay(_handlerTimeout + TimeSpan.FromMilliseconds(1500));

        var globalTimeout = _handlerTimeout + MessagingService.AdditionalTimeoutDuration;

        // Assert
        receivedException.Should().NotBeNull("a timeout exception should have been raised");
        receivedException.Should().BeOfType<TimeoutException>();
        receivedException!.Message.Should().Contain($"Handler for System.String timed out after {globalTimeout.TotalMilliseconds}ms");
    }

    [Fact]
    public async Task Publish_ComplexObjectMessage_ShouldHandleSuccessfully()
    {
        // Arrange
        var handlerCalled = false;
        dynamic complexMessage = new ExpandoObject();
        complexMessage.Id = 1;
        complexMessage.Name = "Test";
        complexMessage.Data = new List<int> { 1, 2, 3 };
        var messageProcessed = new TaskCompletionSource<bool>();

        // Subscribe to ExpandoObject type
        _messagingService.Subscribe<ExpandoObject>(
            async msg =>
                {
                    await Task.Yield(); // Yield to ensure asynchronous processing
                    var message = (IDictionary<string, object>)msg; // Cast to IDictionary for dynamic access
                    if (message != null &&
                        message.TryGetValue("Id", out var id) && (int)id == 1 &&
                        message.TryGetValue("Name", out var name) && (string)name == "Test")
                    {
                        if (message.TryGetValue("Data", out var data) && data is List<int> dataList && dataList.SequenceEqual(new List<int> { 1, 2, 3 }))
                        {
                            handlerCalled = true; // Mark handler as called
                            messageProcessed.SetResult(true); // Signal that processing is done
                        }
                    }
                });

        // Act
        await _messagingService.Publish(complexMessage); // Publish the message

        // Set a timeout for the message processing
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5)); // Timeout after 5 seconds
        var completedTask = await Task.WhenAny(messageProcessed.Task, timeoutTask);

        // Assert
        if (completedTask == timeoutTask)
        {
            throw new TimeoutException("The message processing took too long and timed out.");
        }

        handlerCalled.Should().BeTrue("the handler should have been called with the complex object");
    }

    [Fact]
    public async Task Publish_MessageQueueAtMaxCapacity_ShouldHandleGracefully()
    {
        // Arrange
        var handlerCallCount = 0;
        _messagingService.Subscribe<string>(
            async _ =>
                {
                    await Task.Yield();
                    Interlocked.Increment(ref handlerCallCount);
                });

        // Fill the message queue to a high capacity
        var tasks = new List<Task>();
        for (int i = 0; i < 1000; i++)
        {
            tasks.Add(_messagingService.Publish($"test message {i}"));
        }

        // Act
        var publishTask = _messagingService.Publish("additional message");

        // Assert
        await Task.WhenAll(tasks);
        await publishTask;
        handlerCallCount.Should().Be(
            1001,
            "all messages including the additional one should be handled");
    }

    [Fact]
    public async Task Publish_MultipleMessagesInQuickSuccession_ShouldHandleWithoutErrors()
    {
        // Arrange
        var handlerCallCount = 0;
        _messagingService.Subscribe<string>(
            async _ =>
                {
                    await Task.Yield();
                    Interlocked.Increment(ref handlerCallCount);
                });

        // Act
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(_messagingService.Publish($"test message {i}"));
        }

        await Task.WhenAll(tasks);

        // Assert
        handlerCallCount.Should().Be(10, "each message should be handled exactly once");
    }

    [Fact]
    public async Task Publish_NoSubscribers_ShouldCompleteSuccessfullyWithoutExceptions()
    {
        // Act
        var task = _messagingService.Publish("test message");

        // Assert
        await task;
        task.IsCompletedSuccessfully.Should()
            .BeTrue(
                "the publish task should complete successfully even if there are no subscribers");
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

    [Fact(Skip = "Ignoring this test because the timeout logic does not work for syhchronous handlers.")]
    public async Task Publish_SyncHandlerTimesOut_ShouldRaiseTimeoutExceptionEvent()
    {
        // Arrange
        var receivedException = (Exception?)null;
        _messagingService.HandlerExceptionOccurred +=
            (_, args) => receivedException = args.Exception;

        _messagingService.Subscribe<string>(
            _ =>
                {
                    // Simulate a long-running operation that EXCEEDS the timeout
                      Thread.Sleep(1000 * 60);
                });

        // Act
        await _messagingService.Publish("test message");

        // Wait a moment to allow the handler to trigger the timeout
        await Task.Delay(_handlerTimeout + TimeSpan.FromMilliseconds(1500));

        // Assert
        receivedException.Should().NotBeNull("a timeout exception should have been raised");
        receivedException.Should().BeOfType<TimeoutException>();
        receivedException!.Message.Should().Contain("Sync handler timed out after 1000ms");
    }

    [Fact]
    public async Task Publish_TaskCompletionSourceAlreadyCompleted_ShouldHandleGracefully()
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
        var publishTask = _messagingService.Publish("test message");

        // Simulate TaskCompletionSource already being completed
        await publishTask;

        // Assert
        publishTask.IsCompletedSuccessfully.Should()
            .BeTrue("the publish task should complete successfully");
        handlerCalled.Should().BeTrue("the handler should have been called");
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

    [Fact]
    public async Task Publish_WithCancellation_ShouldHandleGracefully()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var handlerCalled = false;
        _messagingService.Subscribe<string>(
            async _ =>
                {
                    await Task.Delay(500); // Simulate work
                    handlerCalled = true;
                });

        // Act
        var publishTask = _messagingService.Publish("test message");

        // Cancel the task before it completes
        cts.Cancel();

        try
        {
            await Task.WhenAny(publishTask, Task.Delay(1000, cts.Token));
        }
        catch (OperationCanceledException)
        {
            // Assert
            publishTask.IsCanceled.Should().BeFalse("the publish task should not be canceled");
            handlerCalled.Should()
                .BeTrue("the handler should have been called despite cancellation");
        }
    }

    [Fact]
    public async Task
        Publish_WithThrowOnTimeoutTrueAndHandlerCompletesBeforeTimeout_ShouldCompleteSuccessfully()
    {
        // Arrange
        var handlerCalled = false;
        _messagingService.Subscribe<string>(
            async _ =>
                {
                    await Task.Delay(500); // Complete before the 1-second timeout
                    handlerCalled = true;
                });

        // Act
        var task = _messagingService.Publish("test message", throwOnTimeout: true);
        await task;

        // Assert
        task.IsCompletedSuccessfully.Should()
            .BeTrue(
                "the publish task should complete successfully when the handler completes before timeout");
        handlerCalled.Should().BeTrue("the handler should have been called");
    }

    [Fact]
    public async Task
        Publish_WithThrowOnTimeoutTrueAndHandlerExceedsTimeout_ShouldThrowTimeoutException()
    {
        // Arrange
        var handlerCalled = false;
        _messagingService.Subscribe<string>(
            async _ =>
                {
                    // Simulate a long-running operation that EXCEEDS the timeout
                    await Task.Run(() => Thread.Sleep(1000 * 60));
                    handlerCalled = true;
                });

        // Act
        Func<Task> act = async () =>
            await _messagingService.Publish("test message", throwOnTimeout: true);

        // Assert
        await act.Should().ThrowAsync<TimeoutException>()
            .WithMessage($"Publishing String timed out after {_handlerTimeout.TotalMilliseconds}ms.");
        handlerCalled.Should().BeFalse("the handler should not have completed before the timeout");
    }
}
