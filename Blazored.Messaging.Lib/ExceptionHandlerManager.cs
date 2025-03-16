namespace Blazor.Messaging;

public class ExceptionHandlerManager
{
    private readonly UniqueEventHandlerManager<HandlerExceptionEventArgs> _eventHandlerManager = new();

    public event EventHandler<HandlerExceptionEventArgs> HandlerExceptionOccurred
    {
        add => _eventHandlerManager.Event += value;
        remove => _eventHandlerManager.Event -= value;
    }

    public void OnHandlerException(HandlerExceptionEventArgs e)
    {
        _eventHandlerManager.Invoke(this, e);
    }
}