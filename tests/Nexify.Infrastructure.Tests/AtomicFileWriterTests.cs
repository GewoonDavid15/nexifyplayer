using Nexify.Infrastructure;
using Xunit;

namespace Nexify.Infrastructure.Tests;

[Collection("Environment")]
public class AtomicFileWriterTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose()
    {
        AtomicFileWriter.FailAfterTempWriteForTests = null;
        _dir.Dispose();
    }

    [Fact]
    public void Write_NewFile_CreatesIt()
    {
        var path = _dir.Combine("a.json");
        AtomicFileWriter.WriteAllText(path, "hello");
        Assert.Equal("hello", File.ReadAllText(path));
    }

    [Fact]
    public void Write_Existing_ReplacesContents()
    {
        var path = _dir.Combine("a.json");
        File.WriteAllText(path, "old");
        AtomicFileWriter.WriteAllText(path, "new");
        Assert.Equal("new", File.ReadAllText(path));
    }

    [Fact]
    public void Write_FailureAfterTemp_LeavesOriginalIntact()
    {
        var path = _dir.Combine("a.json");
        File.WriteAllText(path, "old");
        AtomicFileWriter.FailAfterTempWriteForTests = _ => throw new IOException("boom");
        Assert.Throws<IOException>(() => AtomicFileWriter.WriteAllText(path, "new"));
        Assert.Equal("old", File.ReadAllText(path));
    }

    [Fact]
    public void Write_LeavesNoTempFileOnSuccess()
    {
        var path = _dir.Combine("a.json");
        AtomicFileWriter.WriteAllText(path, "x");
        AtomicFileWriter.WriteAllText(path, "y");
        Assert.False(File.Exists(path + ".tmp"));
    }
}
