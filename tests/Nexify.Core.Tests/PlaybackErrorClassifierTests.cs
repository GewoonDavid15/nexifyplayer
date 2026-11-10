using Xunit;
using Nexify.Core.Playback;

namespace Nexify.Core.Tests;

public class PlaybackErrorClassifierTests
{
    [Fact]
    public void Missing_IsFileNotFound()
    {
        var path = Path.Combine(Path.GetTempPath(), "nexify-missing-" + Guid.NewGuid().ToString("N") + ".mp3");
        Assert.Equal(PlaybackErrorReason.FileNotFound, PlaybackErrorClassifier.ClassifyBeforeOpen(path));
    }

    [Fact]
    public void MissingDirectory_IsFileNotFound()
    {
        var path = Path.Combine(Path.GetTempPath(), "nexify-missing-" + Guid.NewGuid().ToString("N"), "a.mp3");
        Assert.Equal(PlaybackErrorReason.FileNotFound, PlaybackErrorClassifier.ClassifyBeforeOpen(path));
    }

    [Fact]
    public void Existing_IsUnknown()
    {
        var path = Path.Combine(Path.GetTempPath(), "nexify-existing-" + Guid.NewGuid().ToString("N") + ".mp3");
        File.WriteAllBytes(path, [1, 2, 3]);
        try
        {
            Assert.Equal(PlaybackErrorReason.Unknown, PlaybackErrorClassifier.ClassifyBeforeOpen(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExistingButLockedForWriting_IsUnknown()
    {
        // Another process holding the file open for writing must not block playback.
        var path = Path.Combine(Path.GetTempPath(), "nexify-locked-" + Guid.NewGuid().ToString("N") + ".mp3");
        File.WriteAllBytes(path, [1, 2, 3]);
        try
        {
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                Assert.Equal(PlaybackErrorReason.Unknown, PlaybackErrorClassifier.ClassifyBeforeOpen(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
