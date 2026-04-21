using System.IO;
using Serilog;

namespace Assetra.WPF.Infrastructure;

internal static class AppLogging
{
    public static void Configure(string logDir)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(
                Path.Combine(logDir, "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();
    }
}
