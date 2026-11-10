using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Nexify.Presentation;

/// <summary>The pages of the shell (spec 3.1). The sidebar only lists these three in this subproject.</summary>
public enum Page { Home, NowPlaying, Settings }

/// <summary>Navigation state of the main window: the current page and the sidebar mode.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    /// <summary>Below this window width (DIP) the sidebar collapses to icons only (spec 3.4).</summary>
    public const double CompactBelowWidth = 900;

    public ShellViewModel()
    {
        NavigateCommand = new RelayCommand<Page>(page => CurrentPage = page);
    }

    [ObservableProperty]
    private Page _currentPage = Page.Home;

    [ObservableProperty]
    private bool _isSidebarCompact;

    public RelayCommand<Page> NavigateCommand { get; }

    public void UpdateForWidth(double width) => IsSidebarCompact = width < CompactBelowWidth;
}
