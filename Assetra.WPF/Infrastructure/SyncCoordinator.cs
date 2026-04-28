using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Assetra.Application.Sync;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Assetra.Infrastructure.Sync.Http;

namespace Assetra.WPF.Infrastructure;

/// <summary>
/// 給 Sync Settings UI 觸發手動同步用的協調器：每次呼叫 <see cref="SyncAsync"/> 依當前
/// <see cref="IAppSettingsService.Current"/> + 使用者輸入的密語動態組裝完整同步管線：
/// <para>
/// <c>HttpCloudSyncProvider</c> → <c>EncryptingCloudSyncProvider</c>（AES-GCM）→
/// <c>SyncOrchestrator</c>（pull/resolve/push/save）+ <c>CategoryLocalChangeQueue</c> +
/// <c>JsonSyncMetadataStore</c>。
/// </para>
/// <para>
/// 密語**只活在 method 參數**：unique 過 KDF → AES key → 用完即丟，不寫進 settings、不留在 ViewModel。
/// 第一次同步時會自動產生 <see cref="AppSettings.SyncDeviceId"/> / <see cref="AppSettings.SyncPassphraseSalt"/>
/// 並透過 <see cref="IAppSettingsService.SaveAsync"/> 持久化。
/// </para>
/// </summary>
public sealed class SyncCoordinator
{
    private const int SaltLengthBytes = 16;
    private const int KeyLengthBytes = 32;

    private readonly IAppSettingsService _settings;
    private readonly ILocalChangeQueue _queue;
    private readonly IConflictResolver _resolver;
    private readonly Func<HttpClient>? _httpClientFactory;
    private readonly string _metadataPath;

    public SyncCoordinator(
        IAppSettingsService settings,
        ILocalChangeQueue queue,
        IConflictResolver resolver,
        string metadataPath,
        Func<HttpClient>? httpClientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentException.ThrowIfNullOrEmpty(metadataPath);

        _settings = settings;
        _queue = queue;
        _resolver = resolver;
        _metadataPath = metadataPath;
        _httpClientFactory = httpClientFactory;
    }

    public ILocalChangeQueue Queue => _queue;

    public async Task<SyncResult> SyncAsync(string passphrase, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(passphrase);
        var current = _settings.Current;

        if (!current.SyncEnabled)
            throw new InvalidOperationException("Sync is not enabled in settings.");
        if (string.IsNullOrEmpty(current.SyncBackendUrl))
            throw new InvalidOperationException("Backend URL is not configured.");

        // First-run bootstrap: device id + salt
        var (deviceId, saltBase64, settingsChanged) = EnsureDeviceIdAndSalt(current);
        if (settingsChanged)
            await _settings.SaveAsync(current with
            {
                SyncDeviceId = deviceId,
                SyncPassphraseSalt = saltBase64,
            }).ConfigureAwait(false);

        var salt = Convert.FromBase64String(saltBase64);
        var kdf = new Pbkdf2KeyDerivationService();
        var key = kdf.DeriveKey(passphrase, salt, KeyLengthBytes);

        using var encryption = new AesGcmEncryptionService(key);
        Array.Clear(key, 0, key.Length); // service has its own copy; clear our reference

        var http = _httpClientFactory?.Invoke() ?? new HttpClient();
        try
        {
            var endpoint = new SyncEndpointOptions(current.SyncBackendUrl, current.SyncAuthToken);
            var inner = new HttpCloudSyncProvider(http, endpoint);
            var encrypted = new EncryptingCloudSyncProvider(inner, encryption);

            using var metadataStore = new JsonSyncMetadataStore(_metadataPath, deviceId);
            var orch = new SyncOrchestrator(encrypted, _queue, metadataStore, _resolver);

            return await orch.SyncAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            if (_httpClientFactory is null) http.Dispose();
        }
    }

    private static (string DeviceId, string SaltBase64, bool Changed) EnsureDeviceIdAndSalt(AppSettings current)
    {
        var deviceId = current.SyncDeviceId;
        var salt = current.SyncPassphraseSalt;
        var changed = false;

        if (string.IsNullOrEmpty(deviceId))
        {
            deviceId = Guid.NewGuid().ToString();
            changed = true;
        }

        if (string.IsNullOrEmpty(salt))
        {
            var bytes = new byte[SaltLengthBytes];
            RandomNumberGenerator.Fill(bytes);
            salt = Convert.ToBase64String(bytes);
            changed = true;
        }

        return (deviceId, salt, changed);
    }
}
