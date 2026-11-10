using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Nexify.Presentation.Tests;

public class NotificationsViewModelTests
{
    [Fact]
    public void Notifications_Max3_AutoDismissAfter6s()
    {
        var time = new FakeTimeProvider();
        var vm = new NotificationsViewModel(new SyncDispatcher(), time);

        vm.Show("1");
        time.Advance(TimeSpan.FromSeconds(1));
        vm.Show("2", NotificationKind.Success);
        vm.Show("3", NotificationKind.Error);
        vm.Show("4");

        Assert.Equal(["2", "3", "4"], vm.Visible.Select(n => n.Message));
        Assert.Equal(NotificationKind.Success, vm.Visible[0].Kind);

        time.Advance(TimeSpan.FromSeconds(5.9)); // t = 6.9: "1" (pushed out) would have expired at 6 s; 2-4 expire at 7 s
        Assert.Equal(3, vm.Visible.Count);

        time.Advance(TimeSpan.FromSeconds(0.2)); // t = 7.1
        Assert.Empty(vm.Visible);
    }

    [Fact]
    public void Dismiss_RemovesItem()
    {
        var vm = new NotificationsViewModel(new SyncDispatcher(), new FakeTimeProvider());
        vm.Show("a");
        vm.Show("b");

        vm.DismissCommand.Execute(vm.Visible[0]);

        Assert.Equal("b", Assert.Single(vm.Visible).Message);
    }

    [Fact]
    public void Show_FromAnyThread_GoesThroughDispatcher()
    {
        var posted = new List<Action>();
        var vm = new NotificationsViewModel(new QueueDispatcher(posted), new FakeTimeProvider());

        vm.Show("x");
        Assert.Empty(vm.Visible);

        posted.ForEach(a => a());
        Assert.Single(vm.Visible);
    }

    private sealed class QueueDispatcher(List<Action> posted) : IUiDispatcher
    {
        public void Post(Action action) => posted.Add(action);
    }
}
