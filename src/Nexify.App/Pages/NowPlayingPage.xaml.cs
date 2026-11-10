using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Nexify.Presentation;

namespace Nexify.App.Pages;

/// <summary>
/// Now playing (spec 3.1): video or audio panel and the queue. Queue rows are reordered with own drag handlers
/// (no extra package); an adorner shows where the row will land.
/// </summary>
public partial class NowPlayingPage : UserControl
{
    private const string DragFormat = "Nexify.QueueItem";

    private PlayerViewModel? _player;
    private Point _dragStart;
    private QueueItemViewModel? _dragCandidate;
    private InsertionAdorner? _adorner;

    public NowPlayingPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_player is not null) _player.PropertyChanged -= OnPlayerPropertyChanged;
        _player = e.NewValue as PlayerViewModel;
        if (_player is not null) _player.PropertyChanged += OnPlayerPropertyChanged;
        AttachVideo();
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.VideoSource)) AttachVideo();
    }

    /// <summary>The engine exposes its LibVLC MediaPlayer as an object (Presentation has no LibVLC reference).</summary>
    private void AttachVideo()
    {
        var mediaPlayer = _player?.VideoSource as LibVLCSharp.Shared.MediaPlayer;
        if (!ReferenceEquals(Video.MediaPlayer, mediaPlayer)) Video.MediaPlayer = mediaPlayer;
    }

    // ------------------------------------------------------------------ play / context menu / keys

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && sender is ListBoxItem { DataContext: QueueItemViewModel row })
        {
            _player?.PlayQueueItemCommand.Execute(row);
            e.Handled = true;
        }
    }

    private void OnPlayNextClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: QueueItemViewModel row }) _player?.PlayNextQueueItemCommand.Execute(row);
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: QueueItemViewModel row }) _player?.RemoveQueueItemCommand.Execute(row);
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (QueueList.SelectedItem is not QueueItemViewModel row || _player is null) return;
        if (e.Key == Key.Enter)
        {
            _player.PlayQueueItemCommand.Execute(row);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            var index = QueueList.SelectedIndex;
            _player.RemoveQueueItemCommand.Execute(row);
            if (QueueList.Items.Count > 0)
            {
                QueueList.SelectedIndex = Math.Min(index, QueueList.Items.Count - 1);
                (QueueList.ItemContainerGenerator.ContainerFromIndex(QueueList.SelectedIndex) as ListBoxItem)?.Focus();
            }

            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ drag to reorder

    private void OnListPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(QueueList);
        _dragCandidate = ContainerAt(e.OriginalSource as DependencyObject)?.DataContext as QueueItemViewModel;
    }

    private void OnListPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate is null || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(QueueList) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var row = _dragCandidate;
        _dragCandidate = null;
        try
        {
            Serilog.Log.Information("Queue drag started for {Title}", row.Title);
            var result = DragDrop.DoDragDrop(QueueList, new DataObject(DragFormat, row), DragDropEffects.Move);
            Serilog.Log.Information("Queue drag ended: {Result}", result);
        }
        finally
        {
            RemoveAdorner();
        }
    }

    private void OnListDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DragFormat))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        var (_, y) = InsertionPoint(e.GetPosition(QueueList));
        ShowAdorner(y);
        AutoScroll(e.GetPosition(QueueList));
    }

    private void OnListDragLeave(object sender, DragEventArgs e)
    {
        var p = e.GetPosition(QueueList);
        if (p.X < 0 || p.Y < 0 || p.X >= QueueList.ActualWidth || p.Y >= QueueList.ActualHeight) RemoveAdorner();
    }

    private void OnListDrop(object sender, DragEventArgs e)
    {
        RemoveAdorner();
        if (_player is null || e.Data.GetData(DragFormat) is not QueueItemViewModel row) return;
        var from = _player.QueueItems.IndexOf(row);
        if (from < 0) return;

        var (insertAt, _) = InsertionPoint(e.GetPosition(QueueList));
        var to = insertAt > from ? insertAt - 1 : insertAt;
        Serilog.Log.Information("Queue drop at {Point}: move {From} -> {To}", e.GetPosition(QueueList), from, to);
        _player.MoveQueueItem(from, to);
        QueueList.SelectedItem = row;
        e.Handled = true;
    }

    /// <summary>Insertion index (0..Count) under <paramref name="point"/> and the y (in list coordinates) of the line to draw.</summary>
    private (int Index, double Y) InsertionPoint(Point point)
    {
        var count = QueueList.Items.Count;
        double lastBottom = 0;
        for (var i = 0; i < count; i++)
        {
            if (QueueList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem item || !item.IsVisible) continue;
            var top = item.TranslatePoint(new Point(0, 0), QueueList).Y;
            var bottom = top + item.ActualHeight;
            lastBottom = bottom;
            if (point.Y < top + item.ActualHeight / 2) return (i, top - 1);
        }

        return (count, lastBottom);
    }

    private void AutoScroll(Point point)
    {
        if (FindChild<ScrollViewer>(QueueList) is not { } scroll) return;
        const double edge = 28;
        if (point.Y < edge) scroll.ScrollToVerticalOffset(scroll.VerticalOffset - 8);
        else if (point.Y > QueueList.ActualHeight - edge) scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 8);
    }

    private void ShowAdorner(double y)
    {
        if (_adorner is null)
        {
            var layer = AdornerLayer.GetAdornerLayer(QueueList);
            if (layer is null) return;
            _adorner = new InsertionAdorner(QueueList, (Brush)FindResource("Accent"));
            layer.Add(_adorner);
        }

        _adorner.Y = y;
    }

    private void RemoveAdorner()
    {
        if (_adorner is null) return;
        AdornerLayer.GetAdornerLayer(QueueList)?.Remove(_adorner);
        _adorner = null;
    }

    private static ListBoxItem? ContainerAt(DependencyObject? source)
    {
        for (var d = source; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is ListBoxItem item) return item;
        }

        return null;
    }

    private static T? FindChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } found) return found;
        }

        return null;
    }

    /// <summary>A 2 px accent line with round end dots showing where a dragged queue row will be inserted.</summary>
    private sealed class InsertionAdorner : Adorner
    {
        private readonly Pen _pen;
        private double _y;

        public InsertionAdorner(UIElement adorned, Brush brush)
            : base(adorned)
        {
            _pen = new Pen(brush, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            IsHitTestVisible = false;
        }

        public double Y
        {
            get => _y;
            set
            {
                if (_y.Equals(value)) return;
                _y = value;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            var width = ((FrameworkElement)AdornedElement).ActualWidth;
            var y = Math.Max(1, _y);
            dc.DrawLine(_pen, new Point(8, y), new Point(width - 8, y));
            dc.DrawEllipse(_pen.Brush, null, new Point(8, y), 3, 3);
        }
    }
}
