using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Nexify.Presentation;

namespace Nexify.App.Player;

/// <summary>
/// Timeline slider (spec 3.4). Follows <see cref="Position"/> from the engine except while the thumb is dragged;
/// during a drag a tooltip shows the target time, and on release <see cref="SeekCommand"/> runs with the new
/// position in seconds. A click on the track (IsMoveToPointEnabled) seeks straight away.
/// </summary>
public sealed class SeekSlider : Slider
{
    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(double), typeof(SeekSlider), new PropertyMetadata(0d, (d, _) => ((SeekSlider)d).SyncFromPosition()));

    public static readonly DependencyProperty SeekCommandProperty = DependencyProperty.Register(
        nameof(SeekCommand), typeof(ICommand), typeof(SeekSlider));

    private readonly ToolTip _tip;
    private bool _dragging;
    private bool _syncing;
    private bool _userInput;

    public SeekSlider()
    {
        IsMoveToPointEnabled = true;
        // Not a tab stop: the arrow keys seek through the app-wide shortcuts (spec 3.6) instead.
        Focusable = false;
        _tip = new ToolTip { Placement = PlacementMode.Top, VerticalOffset = -6 };
    }

    /// <summary>Playback position in seconds (one-way from the view model).</summary>
    public double Position
    {
        get => (double)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    /// <summary>Executed with the target position in seconds (a boxed double).</summary>
    public ICommand? SeekCommand
    {
        get => (ICommand?)GetValue(SeekCommandProperty);
        set => SetValue(SeekCommandProperty, value);
    }

    private Thumb? Thumb => (GetTemplateChild("PART_Track") as Track)?.Thumb;

    /// <summary>Keeps the value in step when the duration arrives after the position.</summary>
    protected override void OnMaximumChanged(double oldMaximum, double newMaximum)
    {
        base.OnMaximumChanged(oldMaximum, newMaximum);
        SyncFromPosition();
    }

    protected override void OnThumbDragStarted(DragStartedEventArgs e)
    {
        _dragging = true;
        base.OnThumbDragStarted(e);
        _tip.PlacementTarget = Thumb;
        UpdateTip();
        _tip.IsOpen = true;
    }

    protected override void OnThumbDragDelta(DragDeltaEventArgs e)
    {
        base.OnThumbDragDelta(e);
        UpdateTip();
    }

    protected override void OnThumbDragCompleted(DragCompletedEventArgs e)
    {
        base.OnThumbDragCompleted(e);
        _tip.IsOpen = false;
        _dragging = false;
        Seek(Value);
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        if (_dragging)
        {
            UpdateTip();
        }
        else if (_userInput && !_syncing)
        {
            // Click on the track: seek right away. Other changes (e.g. the maximum shrinking
            // when a new item opens coerces the value) must not seek.
            Seek(newValue);
        }
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _userInput = true;
        try
        {
            base.OnPreviewMouseLeftButtonDown(e);
        }
        finally
        {
            _userInput = false;
        }
    }

    private void Seek(double seconds)
    {
        if (SeekCommand is { } command && command.CanExecute(seconds))
        {
            command.Execute(seconds);
        }
    }

    private void SyncFromPosition()
    {
        if (_dragging)
        {
            return;
        }

        _syncing = true;
        try
        {
            Value = Math.Min(Position, Maximum);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void UpdateTip()
    {
        _tip.Content = PlayerViewModel.FormatTime(Value);
        // Re-place the popup over the moving thumb.
        var offset = _tip.HorizontalOffset;
        _tip.HorizontalOffset = offset + 1;
        _tip.HorizontalOffset = offset;
    }
}
