using Xunit;

namespace Nexify.Playback.Tests;

internal static class TestMedia
{
    public const string EnvVar = "NEXIFY_TEST_MEDIA";

    public static string? Dir => Environment.GetEnvironmentVariable(EnvVar) is { Length: > 0 } d ? d : null;

    public static string? SkipReason =>
        !OperatingSystem.IsWindows() ? "Playback integration tests run on Windows only."
        : Dir is null ? $"{EnvVar} is not set (run tests/media/generate-test-media.ps1)."
        : null;

    public static string PathOf(string name)
    {
        var path = System.IO.Path.Combine(Dir ?? throw new InvalidOperationException($"{EnvVar} is not set."), name);
        if (!File.Exists(path)) throw new FileNotFoundException($"Test media file missing: {path}", path);
        return path;
    }
}

/// <summary>A fact that is skipped unless running on Windows with NEXIFY_TEST_MEDIA set.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (TestMedia.SkipReason is { } reason) Skip = reason;
    }
}

/// <summary>A theory that is skipped unless running on Windows with NEXIFY_TEST_MEDIA set.</summary>
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (TestMedia.SkipReason is { } reason) Skip = reason;
    }
}
