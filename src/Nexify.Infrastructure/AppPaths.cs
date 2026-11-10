namespace Nexify.Infrastructure;

/// <summary>Locations of user data and logs. NEXIFY_DATA_DIR overrides both (logs go to &lt;dir&gt;/logs).</summary>
public sealed class AppPaths
{
    public const string EnvVar = "NEXIFY_DATA_DIR";
    private const string AppFolder = "Nexify Player";

    public AppPaths(string dataDir, string logDir)
    {
        DataDir = dataDir;
        LogDir = logDir;
    }

    public string DataDir { get; }
    public string LogDir { get; }
    public string SettingsFile => Path.Combine(DataDir, "settings.json");
    public string RecentFile => Path.Combine(DataDir, "recent.json");

    public static AppPaths Resolve()
    {
        var overrideDir = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(overrideDir))
            return new AppPaths(overrideDir, Path.Combine(overrideDir, "logs"));

        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new AppPaths(
            Path.Combine(roaming, AppFolder),
            Path.Combine(local, AppFolder, "logs"));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(LogDir);
    }
}
