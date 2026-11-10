using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;

namespace Nexify.Presentation;

/// <summary>One toast. Compared by reference, so two identical messages are separate toasts.</summary>
public sealed class NotificationItem(string message, NotificationKind kind)
{
    public string Message { get; } = message;
    public NotificationKind Kind { get; } = kind;
}

/// <summary>
/// The visible toasts (spec 5): at most 3 at a time (a 4th pushes out the oldest), each removed after 6 s
/// or when dismissed. <see cref="Show"/> may be called from any thread; the collection only changes on the UI thread.
/// </summary>
public sealed class NotificationsViewModel : INotificationService
{
    public const int MaxVisible = 3;
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(6);

    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _time;
    private readonly Dictionary<NotificationItem, ITimer> _timers = [];

    public NotificationsViewModel(IUiDispatcher dispatcher, TimeProvider? time = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _time = time ?? TimeProvider.System;
        DismissCommand = new RelayCommand<NotificationItem>(item =>
        {
            if (item is not null) Remove(item);
        });
    }

    /// <summary>Oldest first.</summary>
    public ObservableCollection<NotificationItem> Visible { get; } = [];

    public RelayCommand<NotificationItem> DismissCommand { get; }

    public void Show(string message, NotificationKind kind = NotificationKind.Info)
    {
        ArgumentNullException.ThrowIfNull(message);
        _dispatcher.Post(() => Add(new NotificationItem(message, kind)));
    }

    private void Add(NotificationItem item)
    {
        while (Visible.Count >= MaxVisible) Remove(Visible[0]);
        Visible.Add(item);
        _timers[item] = _time.CreateTimer(_ => _dispatcher.Post(() => Remove(item)), null, Lifetime, Timeout.InfiniteTimeSpan);
    }

    private void Remove(NotificationItem item)
    {
        if (_timers.Remove(item, out var timer)) timer.Dispose();
        Visible.Remove(item);
    }
}
