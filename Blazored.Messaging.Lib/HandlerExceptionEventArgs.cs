namespace Blazored.Messaging;

public class HandlerExceptionEventArgs : EventArgs
{
  public Exception Exception { get; }
  public Type MessageType { get; }
  public object Handler { get; } // Action or Func

  public HandlerExceptionEventArgs(Exception exception, Type messageType, object handler)
  {
    Exception = exception;
    MessageType = messageType;
    Handler = handler;
  }
}