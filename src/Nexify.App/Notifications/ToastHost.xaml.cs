using System.Windows.Controls;

namespace Nexify.App.Notifications;

/// <summary>In-app toasts (spec 5). DataContext: <see cref="Nexify.Presentation.NotificationsViewModel"/>.</summary>
public partial class ToastHost : UserControl
{
    public ToastHost()
    {
        InitializeComponent();
    }
}
