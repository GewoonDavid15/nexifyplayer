using Nexify.Infrastructure;
using Xunit;

namespace Nexify.Infrastructure.Tests;

[Collection("Environment")]
public class AppPathsTests
{
    private const string Var = "NEXIFY_DATA_DIR";

    [Fact]
    public void Resolve_UsesEnvOverride()
    {
        var old = Environment.GetEnvironmentVariable(Var);
        try
        {
            Environment.SetEnvironmentVariable(Var, "/tmp/x");
            var p = AppPaths.Resolve();
            Assert.Equal("/tmp/x", p.DataDir);
            Assert.Equal(Path.Combine("/tmp/x", "logs"), p.LogDir);
            Assert.Equal(Path.Combine("/tmp/x", "settings.json"), p.SettingsFile);
            Assert.Equal(Path.Combine("/tmp/x", "recent.json"), p.RecentFile);
        }
        finally { Environment.SetEnvironmentVariable(Var, old); }
    }

    [Fact]
    public void EnsureCreated_CreatesBothDirectories()
    {
        using var t = new TempDir();
        var p = new AppPaths(t.Combine("data"), t.Combine("logs"));
        p.EnsureCreated();
        Assert.True(Directory.Exists(p.DataDir));
        Assert.True(Directory.Exists(p.LogDir));
    }
}
