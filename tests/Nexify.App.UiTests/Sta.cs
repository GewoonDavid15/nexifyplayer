using System.Runtime.ExceptionServices;
using Xunit;

namespace Nexify.App.UiTests;

/// <summary>A fact that only runs on Windows (WPF).</summary>
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "WPF tests run on Windows only.";
    }
}

internal static class Sta
{
    /// <summary>Runs <paramref name="action"/> on a fresh STA thread and rethrows its exception.</summary>
    public static void Run(Action action)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }
}
