using Xunit;
using Nexify.Core.Media;

namespace Nexify.Core.Tests;

public class MediaFormatsTests
{
    [Theory]
    [InlineData(".mp3")] [InlineData(".wav")] [InlineData(".flac")] [InlineData(".aac")] [InlineData(".m4a")]
    [InlineData(".ogg")] [InlineData(".oga")] [InlineData(".opus")] [InlineData(".wma")] [InlineData(".alac")]
    public void Detect_KnownAudio_ReturnsAudio(string ext)
        => Assert.Equal(MediaKind.Audio, MediaFormats.Detect("file" + ext));

    [Theory]
    [InlineData(".mp4")] [InlineData(".m4v")] [InlineData(".mkv")] [InlineData(".avi")] [InlineData(".mov")]
    [InlineData(".wmv")] [InlineData(".webm")] [InlineData(".mpeg")] [InlineData(".mpg")] [InlineData(".ts")] [InlineData(".m2ts")]
    public void Detect_KnownVideo_ReturnsVideo(string ext)
        => Assert.Equal(MediaKind.Video, MediaFormats.Detect("file" + ext));

    [Fact]
    public void Detect_IsCaseInsensitive()
    {
        Assert.Equal(MediaKind.Audio, MediaFormats.Detect("A.MP3"));
        Assert.Equal(MediaKind.Video, MediaFormats.Detect("B.MkV"));
    }

    [Theory]
    [InlineData("a.txt")] [InlineData("a.srt")] [InlineData("")] [InlineData("noext")] [InlineData("map.")]
    public void Detect_Unknown_ReturnsNull(string path)
    {
        Assert.Null(MediaFormats.Detect(path));
        Assert.False(MediaFormats.IsSupported(path));
        Assert.Null(MediaFormats.TryCreate(path));
    }

    [Fact]
    public void TryCreate_SetsDisplayName()
    {
        var item = MediaFormats.TryCreate(@"C:\a\Song One.flac");
        Assert.NotNull(item);
        Assert.Equal("Song One", item!.DisplayName);
        Assert.Equal(MediaKind.Audio, item.Kind);
        Assert.Equal("Song One", MediaFormats.TryCreate("/a/b/Song One.flac")!.DisplayName);
    }

    [Fact]
    public void TwoItemsSamePath_HaveDifferentIds()
    {
        var a = new MediaItem("x.mp3", MediaKind.Audio);
        var b = new MediaItem("x.mp3", MediaKind.Audio);
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
