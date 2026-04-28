namespace Assetra.Infrastructure.Sync.Http;

/// <summary>
/// <see cref="HttpCloudSyncProvider"/> 連線設定。Caller 在 DI 註冊時提供。
/// </summary>
/// <param name="BaseUrl">後端 base URL（例：<c>https://sync.example.workers.dev</c>），不需尾斜線。</param>
/// <param name="AuthToken">Bearer token；空字串 = 不送 Authorization header（測試 / 公開後端）。</param>
public sealed record SyncEndpointOptions(string BaseUrl, string AuthToken = "");
