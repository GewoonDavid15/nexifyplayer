using System.Collections.ObjectModel;
using Nexify.Core.Media;

namespace Nexify.Core.Queue;

/// <summary>
/// Play queue with shuffle and repeat. Not thread-safe; use from the UI thread only.
/// Internally <c>_order</c> holds indexes into <c>_items</c> in play order (identity when
/// shuffle is off) and <c>_pos</c> is the position within <c>_order</c> (-1 = none).
/// </summary>
public sealed class PlayQueue
{
    private readonly Random _random;
    private readonly List<MediaItem> _items = [];
    private readonly ReadOnlyCollection<MediaItem> _view;
    private List<int> _order = [];
    private int _pos = -1;
    private bool _shuffle;
    private RepeatMode _repeat;

    public PlayQueue(Random? random = null)
    {
        _random = random ?? Random.Shared;
        _view = _items.AsReadOnly();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<MediaItem> Items => _view;
    public int CurrentIndex => _pos >= 0 ? _order[_pos] : -1;
    public MediaItem? Current => _pos >= 0 ? _items[_order[_pos]] : null;

    public bool Shuffle
    {
        get => _shuffle;
        set
        {
            if (_shuffle == value) return;
            _shuffle = value;
            var current = CurrentIndex;
            RebuildOrder(current);
            Raise();
        }
    }

    public RepeatMode Repeat
    {
        get => _repeat;
        set
        {
            if (_repeat == value) return;
            _repeat = value;
            Raise();
        }
    }

    public bool HasNext => _pos + 1 < _order.Count || (_repeat == RepeatMode.All && _order.Count > 0);
    public bool HasPrevious => _pos > 0 || (_repeat == RepeatMode.All && _order.Count > 0);

    public void Add(MediaItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
        _order.Add(_items.Count - 1);
        Raise();
    }

    public void AddRange(IEnumerable<MediaItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var added = false;
        foreach (var item in items)
        {
            _items.Add(item);
            _order.Add(_items.Count - 1);
            added = true;
        }
        if (added) Raise();
    }

    public void InsertNext(MediaItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var at = CurrentIndex + 1;
        _items.Insert(at, item);
        for (var i = 0; i < _order.Count; i++)
            if (_order[i] >= at) _order[i]++;
        _order.Insert(_pos + 1, at);
        Raise();
    }

    public void Remove(MediaItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var idx = _items.FindIndex(i => i.Id == item.Id);
        if (idx < 0) return;
        _items.RemoveAt(idx);
        var p = _order.IndexOf(idx);
        _order.RemoveAt(p);
        for (var i = 0; i < _order.Count; i++)
            if (_order[i] > idx) _order[i]--;
        if (p < _pos) _pos--;
        else if (p == _pos && _pos >= _order.Count) _pos = _order.Count - 1;
        Raise();
    }

    public void Move(int from, int to)
    {
        if (from < 0 || from >= _items.Count) throw new ArgumentOutOfRangeException(nameof(from));
        if (to < 0 || to >= _items.Count) throw new ArgumentOutOfRangeException(nameof(to));
        if (from == to) return;
        var item = _items[from];
        _items.RemoveAt(from);
        _items.Insert(to, item);
        for (var i = 0; i < _order.Count; i++)
        {
            var v = _order[i];
            if (v == from) _order[i] = to;
            else if (from < to && v > from && v <= to) _order[i] = v - 1;
            else if (from > to && v >= to && v < from) _order[i] = v + 1;
        }
        // Without shuffle the play order must stay identical to the visible list order.
        if (!_shuffle) RebuildOrder(CurrentIndex);
        Raise();
    }

    public void Clear()
    {
        if (_items.Count == 0 && _pos == -1) return;
        _items.Clear();
        _order = [];
        _pos = -1;
        Raise();
    }

    public void ReplaceWith(IEnumerable<MediaItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items.Clear();
        _items.AddRange(items);
        RebuildOrder(_items.Count > 0 ? 0 : -1);
        Raise();
    }

    public MediaItem? MoveTo(int index)
    {
        if (index < 0 || index >= _items.Count) return null;
        _pos = _order.IndexOf(index);
        Raise();
        return Current;
    }

    public MediaItem? Next()
    {
        if (_order.Count == 0) return null;
        if (_pos + 1 < _order.Count)
        {
            _pos++;
        }
        else if (_repeat == RepeatMode.All)
        {
            if (_shuffle) Reshuffle(avoidFirst: CurrentIndex);
            _pos = 0;
        }
        else
        {
            return null;
        }
        Raise();
        return Current;
    }

    public MediaItem? Previous()
    {
        if (_order.Count == 0) return null;
        if (_pos > 0) _pos--;
        else if (_repeat == RepeatMode.All) _pos = _order.Count - 1;
        else return null;
        Raise();
        return Current;
    }

    /// <summary>Item to play when playback ended by itself: the same item for Repeat.One, otherwise <see cref="Next"/>.</summary>
    public MediaItem? NextForEndReached()
        => _repeat == RepeatMode.One && Current is { } cur ? cur : Next();

    /// <summary>Rebuilds _order for the current Shuffle setting, keeping <paramref name="current"/> (item index) current and first.</summary>
    private void RebuildOrder(int current)
    {
        _order = Enumerable.Range(0, _items.Count).ToList();
        if (_shuffle)
        {
            if (current >= 0)
            {
                _order.RemoveAt(current);
                FisherYates(_order);
                _order.Insert(0, current);
            }
            else
            {
                FisherYates(_order);
            }
            _pos = current >= 0 ? 0 : -1;
        }
        else
        {
            _pos = current;
        }
    }

    private void Reshuffle(int avoidFirst)
    {
        FisherYates(_order);
        if (_order.Count > 1 && _order[0] == avoidFirst)
        {
            var j = _random.Next(1, _order.Count);
            (_order[0], _order[j]) = (_order[j], _order[0]);
        }
    }

    private void FisherYates(List<int> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
