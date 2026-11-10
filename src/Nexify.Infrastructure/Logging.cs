using Serilog;
using Serilog.Events;

namespace Nexify.Infrastructure;

public static class Logging
{
    public static ILogger Configure(AppPaths paths)
    {
        var logger = new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Information)
            .WriteTo.File(
                Path.Combine(paths.LogDir, "nexify-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();
        Log.Logger = logger;
        return logger;
    }
}
