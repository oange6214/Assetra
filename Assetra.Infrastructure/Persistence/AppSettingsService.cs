using System.Text.Json;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Microsoft.Extensions.Logging;

namespace Assetra.Infrastructure.Persistence;

public sealed class AppSettingsService : IAppSettingsService, IDisposable
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Assetra", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<AppSettingsService>? _logger;

    public AppSettings Current { get; private set; }

    public event Action? Changed;

    public AppSettingsService(ILogger<AppSettingsService>? logger = null)
    {
        _logger = logger;
        Current = LoadSettings();
    }

    public async Task SaveAsync(AppSettings settings)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            Current = settings;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(FilePath, json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save settings to {Path}", FilePath);
            throw;
        }
        finally { _lock.Release(); }

        // 在鎖之外觸發，避免訂閱者的同步邏輯拖住其他 Save 呼叫
        try
        { Changed?.Invoke(); }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AppSettingsService.Changed subscriber threw");
        }
    }

    /// <summary>
    /// Synchronous load called from constructor — file is tiny, safe to block.
    /// </summary>
    public static AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings, using defaults: {ex}");
            return new AppSettings();
        }
    }

    public void Dispose() => _lock.Dispose();
}
