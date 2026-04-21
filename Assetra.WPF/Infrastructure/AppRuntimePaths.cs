using System.IO;

namespace Assetra.WPF.Infrastructure;

internal sealed record AppRuntimePaths(
    string DataDir,
    string DbPath,
    string AssetsDir,
    string LogDir)
{
    public static AppRuntimePaths Resolve()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Assetra");
        var logDir = Path.Combine(dataDir, "logs");

        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(logDir);

        return new AppRuntimePaths(
            DataDir: dataDir,
            DbPath: Path.Combine(dataDir, "assetra.db"),
            AssetsDir: Path.Combine(AppContext.BaseDirectory, "Assets"),
            LogDir: logDir);
    }
}
