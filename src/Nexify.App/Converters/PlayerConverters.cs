using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Nexify.Core.Queue;

namespace Nexify.App.Converters;

/// <summary>bool to Visibility. <see cref="Invert"/> flips the input; <see cref="UseHidden"/> gives Hidden instead of
/// Collapsed (keeps layout and, for the video view, its native window).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public bool UseHidden { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is true;
        if (Invert) visible = !visible;
        return visible ? Visibility.Visible : UseHidden ? Visibility.Hidden : Visibility.Collapsed;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>True when repeat is All or One (the repeat button is then shown as active).</summary>
public sealed class RepeatIsOnConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is RepeatMode mode && mode != RepeatMode.Off;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Volume glyph from [Volume, IsMuted]: muted, or one of three levels (Segoe Fluent Icons / MDL2).</summary>
public sealed class VolumeGlyphConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var volume = values.Length > 0 && values[0] is int v ? v : 0;
        var muted = values.Length > 1 && values[1] is true;
        if (muted || volume == 0) return "";
        return volume < 34 ? "" : volume < 67 ? "" : "";
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>[HasCurrent, IsEngineLoading]: the "nothing is playing" hint shows only when neither is true.</summary>
public sealed class NothingPlayingVisibility : IMultiValueConverter
{
    public static NothingPlayingVisibility Instance { get; } = new();

    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
        => values.Any(v => v is true) ? Visibility.Collapsed : Visibility.Visible;

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
