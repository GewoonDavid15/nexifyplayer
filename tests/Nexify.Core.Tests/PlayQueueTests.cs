using Nexify.Core.Media;
using Nexify.Core.Queue;
using Xunit;

namespace Nexify.Core.Tests;

public class PlayQueueTests
{
    private static MediaItem Item(string name) => new($"{name}.mp3", MediaKind.Audio);
    private static List<MediaItem> Items(int n) => Enumerable.Range(0, n).Select(i => Item($"t{i}")).ToList();
    private static PlayQueue Filled(int n, int seed = 42)
    {
        var q = new PlayQueue(new Random(seed));
        q.ReplaceWith(Items(n));
        return q;
    }

    [Fact]
    public void Add_FirstItem_DoesNotSetCurrent_UntilMoveTo()
    {
        var q = new PlayQueue();
        var a = Item("a");
        q.Add(a);
        Assert.Equal(-1, q.CurrentIndex);
        Assert.Null(q.Current);
        Assert.Same(a, q.MoveTo(0));
        Assert.Equal(0, q.CurrentIndex);
        Assert.Same(a, q.Current);
    }

    [Fact]
    public void ReplaceWith_SetsCurrentToFirst()
    {
        var q = new PlayQueue();
        q.Add(Item("old"));
        var items = Items(3);
        q.ReplaceWith(items);
        Assert.Equal(0, q.CurrentIndex);
        Assert.Same(items[0], q.Current);
        Assert.Equal(3, q.Items.Count);
        q.ReplaceWith([]);
        Assert.Equal(-1, q.CurrentIndex);
        Assert.Empty(q.Items);
    }

    [Fact]
    public void Next_AtEnd_RepeatOff_ReturnsNull()
    {
        var q = Filled(2);
        q.Next();
        Assert.Null(q.Next());
        Assert.Equal(1, q.CurrentIndex);
        Assert.False(q.HasNext);
    }

    [Fact]
    public void Next_AtEnd_RepeatAll_WrapsToFirst()
    {
        var q = Filled(3);
        q.Repeat = RepeatMode.All;
        q.Next(); q.Next();
        Assert.True(q.HasNext);
        Assert.Same(q.Items[0], q.Next());
        Assert.Equal(0, q.CurrentIndex);
    }

    [Fact]
    public void NextForEndReached_RepeatOne_ReturnsSameItem()
    {
        var q = Filled(3);
        q.Repeat = RepeatMode.One;
        var cur = q.Current;
        Assert.Same(cur, q.NextForEndReached());
        Assert.Equal(0, q.CurrentIndex);
    }

    [Fact]
    public void NextForEndReached_RepeatOff_BehavesLikeNext()
    {
        var q = Filled(3);
        Assert.Same(q.Items[1], q.NextForEndReached());
    }

    [Fact]
    public void Next_RepeatOne_ManualNextAdvances()
    {
        var q = Filled(3);
        q.Repeat = RepeatMode.One;
        Assert.Same(q.Items[1], q.Next());
        Assert.Equal(1, q.CurrentIndex);
    }

    [Fact]
    public void Previous_AtStart_RepeatOff_ReturnsNull()
    {
        var q = Filled(3);
        Assert.Null(q.Previous());
        Assert.Equal(0, q.CurrentIndex);
        Assert.False(q.HasPrevious);
    }

    [Fact]
    public void Previous_AtStart_RepeatAll_ReturnsLast()
    {
        var q = Filled(3);
        q.Repeat = RepeatMode.All;
        Assert.Same(q.Items[2], q.Previous());
        Assert.Equal(2, q.CurrentIndex);
    }

    [Fact]
    public void Previous_GoesBack()
    {
        var q = Filled(3);
        q.Next(); q.Next();
        Assert.Same(q.Items[1], q.Previous());
    }

    [Fact]
    public void InsertNext_PlacesAfterCurrent()
    {
        var q = Filled(3);
        q.Next(); // current = t1
        var x = Item("x");
        q.InsertNext(x);
        Assert.Same(x, q.Items[2]);
        Assert.Same(q.Items[1], q.Current);
        Assert.Same(x, q.Next());
    }

    [Fact]
    public void InsertNext_WithShuffle_IsNextInPlayOrder()
    {
        var q = Filled(10);
        q.Shuffle = true;
        q.Next(); q.Next();
        var cur = q.Current;
        var x = Item("x");
        q.InsertNext(x);
        Assert.Same(cur, q.Current);
        Assert.Same(x, q.Next());
    }

    [Fact]
    public void Move_KeepsCurrentItem()
    {
        var q = Filled(5);
        q.Next(); q.Next(); // t2
        var cur = q.Current;
        q.Move(0, 4);
        Assert.Same(cur, q.Current);
        Assert.Equal(1, q.CurrentIndex);
        q.Move(4, 0);
        Assert.Same(cur, q.Current);
        q.Move(2, 4); // move the current item itself
        Assert.Same(cur, q.Current);
        Assert.Equal(4, q.CurrentIndex);
    }

    [Fact]
    public void Move_ShuffleOff_PlayOrderFollowsList()
    {
        var q = Filled(4); // a b c d, current first
        var items = q.Items.ToList();
        q.Move(1, 3);      // a c d b
        Assert.Same(items[0], q.Current);
        Assert.Same(items[2], q.Next());
        Assert.Same(items[3], q.Next());
        Assert.Same(items[1], q.Next());
        Assert.Same(items[3], q.Previous());
    }

    [Fact]
    public void Move_ShuffleOff_ToFront_PlayOrderFollowsList()
    {
        var q = Filled(3);
        var items = q.Items.ToList();
        q.Move(2, 0); // c a b
        Assert.Same(items[0], q.Current);
        Assert.Same(items[1], q.Next());
        Assert.Null(q.Next());
    }

    [Fact]
    public void Move_ShuffleOn_KeepsCurrent_AndVisitsEveryItemOnce()
    {
        var q = Filled(10);
        q.Shuffle = true;
        q.Next();
        var cur = q.Current;
        q.Move(0, 9);
        q.Move(5, 2);
        Assert.Same(cur, q.Current);
        var played = new List<Guid>();
        // walk back to the start of the play order, then forward through all
        while (q.Previous() is { } p) played.Clear();
        played.Add(q.Current!.Id);
        while (q.Next() is { } n) played.Add(n.Id);
        Assert.Equal(10, played.Distinct().Count());
        Assert.Equal(10, played.Count);
    }

    [Fact]
    public void Remove_Current_SelectsFollowingItem()
    {
        var q = Filled(3);
        q.Next(); // t1
        var following = q.Items[2];
        q.Remove(q.Current!);
        Assert.Same(following, q.Current);
        Assert.Equal(1, q.CurrentIndex);
    }

    [Fact]
    public void Remove_CurrentLast_SelectsPrevious()
    {
        var q = Filled(3);
        q.MoveTo(2);
        q.Remove(q.Current!);
        Assert.Same(q.Items[1], q.Current);
    }

    [Fact]
    public void Remove_BeforeCurrent_KeepsCurrent()
    {
        var q = Filled(3);
        q.MoveTo(2);
        var cur = q.Current;
        q.Remove(q.Items[0]);
        Assert.Same(cur, q.Current);
        Assert.Equal(1, q.CurrentIndex);
    }

    [Fact]
    public void Remove_LastRemaining_SetsIndexMinusOne()
    {
        var q = Filled(1);
        q.Remove(q.Items[0]);
        Assert.Equal(-1, q.CurrentIndex);
        Assert.Null(q.Current);
    }

    [Fact]
    public void Shuffle_On_VisitsEveryItemExactlyOnce()
    {
        var q = Filled(20);
        q.Shuffle = true;
        var seen = new HashSet<Guid> { q.Current!.Id };
        for (var i = 0; i < 19; i++) seen.Add(q.Next()!.Id);
        Assert.Equal(20, seen.Count);
        Assert.Null(q.Next());
    }

    [Fact]
    public void Shuffle_On_KeepsCurrentFirst_AndListOrder()
    {
        var q = Filled(10);
        q.MoveTo(4);
        var cur = q.Current;
        var before = q.Items.ToList();
        q.Shuffle = true;
        Assert.Same(cur, q.Current);
        Assert.Equal(before.Select(i => i.Id), q.Items.Select(i => i.Id));
        Assert.NotSame(cur, q.Next());
    }

    [Fact]
    public void Shuffle_Off_ContinuesFromCurrentInListOrder()
    {
        var q = Filled(10);
        q.Shuffle = true;
        q.Next(); q.Next(); q.Next();
        var idx = q.CurrentIndex;
        q.Shuffle = false;
        Assert.Equal(idx, q.CurrentIndex);
        Assert.Same(q.Items[idx + 1 < 10 ? idx + 1 : 0], idx + 1 < 10 ? q.Next() : q.Items[0]);
    }

    [Fact]
    public void Shuffle_RepeatAll_Wrap_DoesNotRepeatLastImmediately()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var q = Filled(5, seed);
            q.Repeat = RepeatMode.All;
            q.Shuffle = true;
            for (var i = 0; i < 4; i++) q.Next();
            var last = q.Current;
            Assert.NotSame(last, q.Next());
        }
    }

    [Fact]
    public void SameFileTwice_RemoveRemovesOnlyOne()
    {
        var q = new PlayQueue();
        var a = Item("same");
        var b = Item("same");
        q.AddRange([a, b]);
        Assert.Equal(2, q.Items.Count);
        q.Remove(b);
        Assert.Single(q.Items);
        Assert.Same(a, q.Items[0]);
    }

    [Fact]
    public void Changed_RaisedOnEveryMutation()
    {
        var q = new PlayQueue(new Random(1));
        var n = 0;
        q.Changed += (_, _) => n++;
        void Expect(Action act)
        {
            var before = n;
            act();
            Assert.True(n > before);
        }
        var a = Item("a"); var b = Item("b"); var c = Item("c");
        Expect(() => q.Add(a));
        Expect(() => q.AddRange([b, c]));
        Expect(() => q.InsertNext(Item("d")));
        Expect(() => q.MoveTo(1));
        Expect(() => q.Move(0, 2));
        Expect(() => q.Next());
        Expect(() => q.Previous());
        Expect(() => q.Shuffle = true);
        Expect(() => q.Repeat = RepeatMode.All);
        Expect(() => q.Remove(b));
        Expect(() => q.ReplaceWith([a]));
        Expect(() => q.Clear());
    }

    [Fact]
    public void IsLastInOrder_TrueOnlyAtTheLastPlayPosition()
    {
        var q = new PlayQueue();
        Assert.False(q.IsLastInOrder);
        q.ReplaceWith(Items(3));
        Assert.False(q.IsLastInOrder);
        q.MoveTo(2);
        Assert.True(q.IsLastInOrder);

        q.Shuffle = true; // current becomes first in the shuffled order
        Assert.False(q.IsLastInOrder);
        q.Next();
        q.Next();
        Assert.True(q.IsLastInOrder);
    }
}
