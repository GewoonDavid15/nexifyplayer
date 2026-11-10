using Nexify.Core.Media;
using Xunit;

namespace Nexify.Core.Tests;

public class NaturalStringComparerTests
{
    [Fact]
    public void Natural_SortsNumbersNumerically()
    {
        var list = new List<string> { "10.mp3", "2.mp3", "1.mp3" };
        list.Sort(NaturalStringComparer.Instance);
        Assert.Equal(["1.mp3", "2.mp3", "10.mp3"], list);
    }

    [Fact]
    public void Natural_IsCaseInsensitive()
    {
        var list = new List<string> { "b.mp3", "A.mp3", "c.mp3" };
        list.Sort(NaturalStringComparer.Instance);
        Assert.Equal(["A.mp3", "b.mp3", "c.mp3"], list);
        Assert.Equal(0, NaturalStringComparer.Instance.Compare("ABC", "abc"));
    }

    [Fact]
    public void Natural_MixedTextAndNumbers()
    {
        var list = new List<string> { "Track 10 b", "Track 9", "Track 10 a", "Track 1" };
        list.Sort(NaturalStringComparer.Instance);
        Assert.Equal(["Track 1", "Track 9", "Track 10 a", "Track 10 b"], list);
    }

    [Fact]
    public void Natural_HandlesNullAndLongNumbers()
    {
        Assert.True(NaturalStringComparer.Instance.Compare(null, "a") < 0);
        Assert.True(NaturalStringComparer.Instance.Compare("a99999999999999999999999", "a100000000000000000000000") < 0);
    }
}
