using System.Collections.Concurrent;

namespace Blazor.Messaging;

internal class MessageQueue
{
    private readonly ConcurrentQueue<(Type MsgType, object Msg, TaskCompletionSource<bool> Tcs)>
        _queue = new();

    public bool IsEmpty => _queue.IsEmpty;

    public void Enqueue(Type msgType, object message, TaskCompletionSource<bool> tcs)
    {
        _queue.Enqueue((msgType, message, tcs));
    }

    public bool TryDequeue(out (Type MsgType, object Msg, TaskCompletionSource<bool> Tcs) item)
    {
        return _queue.TryDequeue(out item);
    }
}
