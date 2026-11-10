using System.Windows;
using System.Windows.Controls;

namespace Nexify.App.Player;

/// <summary>The transport bar (spec 3.4). DataContext: <see cref="Nexify.Presentation.PlayerViewModel"/>.</summary>
public partial class TransportBar : UserControl
{
    public TransportBar()
    {
        InitializeComponent();
    }

    /// <summary>The queue button was clicked: the shell opens the Now playing page.</summary>
    public event EventHandler? QueueRequested;

    private void OnQueueClick(object sender, RoutedEventArgs e) => QueueRequested?.Invoke(this, EventArgs.Empty);
}
