namespace Nexify.Presentation;

/// <summary>Marshals work to the UI thread. The WPF implementation posts to the Dispatcher; tests run it synchronously.</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}
