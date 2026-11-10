using Xunit;
using Nexify.Core.Playback;

namespace Nexify.Core.Tests;

public class FileUriTests
{
    [Theory]
    [InlineData(@"C:\m\Beyoncé #1 100%.mp3", "file:///C:/m/Beyonc%C3%A9%20%231%20100%25.mp3")]
    [InlineData(@"C:\m\a%41b.mp3", "file:///C:/m/a%2541b.mp3")] // new Uri(path) would turn this into aAb.mp3
    [InlineData(@"C:\m\[x] {y}; a+b&c.mkv", "file:///C:/m/%5Bx%5D%20%7By%7D%3B%20a%2Bb%26c.mkv")]
    [InlineData(@"\\srv\share\x y#.mp3", "file://srv/share/x%20y%23.mp3")]
    [InlineData("/home/u/a b.mp3", "file:///home/u/a%20b.mp3")]
    public void FromPath_EscapesEverySegment(string path, string expected)
        => Assert.Equal(expected, FileUri.FromPath(path).AbsoluteUri);

    [Theory]
    [InlineData(@"C:\m\Beyoncé #1 100%.mp3")]
    [InlineData(@"C:\m\a%41b.mp3")]
    [InlineData(@"\\srv\share\x y#.mp3")]
    public void FromPath_RoundTripsToLocalPath(string path)
        => Assert.Equal(path, FileUri.FromPath(path).LocalPath);
}
