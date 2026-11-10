using System.Windows.Threading;
using Nexify.Presentation;

namespace Nexify.App.Services;

/// <summary><see cref="IUiDispatcher"/> on top of the WPF <see cref="Dispatcher"/> of the UI thread.</summary>
public sealed class WpfDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        dispatcher.BeginInvoke(action);
    }
}
