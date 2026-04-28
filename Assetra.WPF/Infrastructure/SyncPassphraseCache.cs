namespace Assetra.WPF.Infrastructure;

/// <summary>
/// In-process 密語快取，給背景 sync timer 在使用者最近一次手動同步成功後自動觸發用。
/// <para>
/// 設計原則：
/// <list type="bullet">
///   <item>**只活在記憶體**——應用程式關閉或 <see cref="Clear"/> 後即遺失，不寫 disk。</item>
///   <item>由 <see cref="Features.Settings.SyncSettingsViewModel"/> 在使用者手動同步成功時 <see cref="Set"/>。</item>
///   <item><see cref="BackgroundSyncService"/> 在 timer tick 時 <see cref="TryGet"/>；無快取則跳過該 tick。</item>
/// </list>
/// </para>
/// </summary>
public sealed class SyncPassphraseCache
{
    private readonly object _lock = new();
    private string? _value;

    public void Set(string passphrase)
    {
        ArgumentException.ThrowIfNullOrEmpty(passphrase);
        lock (_lock) _value = passphrase;
    }

    public bool TryGet(out string passphrase)
    {
        lock (_lock)
        {
            passphrase = _value ?? string.Empty;
            return !string.IsNullOrEmpty(_value);
        }
    }

    public void Clear()
    {
        lock (_lock) _value = null;
    }
}
