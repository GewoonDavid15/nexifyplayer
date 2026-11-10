using Nexify.Presentation;
using Xunit;

namespace Nexify.Presentation.Tests;

public class ShellViewModelTests
{
    [Fact]
    public void Navigate_ChangesCurrentPage()
    {
        var vm = new ShellViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.Equal(Page.Home, vm.CurrentPage);

        vm.NavigateCommand.Execute(Page.Settings);
        Assert.Equal(Page.Settings, vm.CurrentPage);
        Assert.Contains(nameof(ShellViewModel.CurrentPage), changed);

        vm.NavigateCommand.Execute(Page.NowPlaying);
        Assert.Equal(Page.NowPlaying, vm.CurrentPage);
    }

    [Theory]
    [InlineData(600, true)]
    [InlineData(899.9, true)]
    [InlineData(900, false)]
    [InlineData(1280, false)]
    public void UpdateForWidth_Below900_Compact_AtOrAbove900_Full(double width, bool compact)
    {
        var vm = new ShellViewModel();
        vm.UpdateForWidth(width);
        Assert.Equal(compact, vm.IsSidebarCompact);
    }

    [Fact]
    public void UpdateForWidth_RaisesPropertyChanged_OnlyOnChange()
    {
        var vm = new ShellViewModel();
        var count = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ShellViewModel.IsSidebarCompact)) count++; };

        vm.UpdateForWidth(1280);
        vm.UpdateForWidth(800);
        vm.UpdateForWidth(700);
        vm.UpdateForWidth(1000);

        Assert.Equal(2, count);
    }
}
