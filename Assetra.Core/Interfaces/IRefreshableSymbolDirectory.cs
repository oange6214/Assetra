namespace Assetra.Core.Interfaces;

public interface IRefreshableSymbolDirectory : ISymbolDirectory
{
    string SourceName { get; }
    int Count { get; }
    DateTimeOffset? LastUpdatedAt { get; }
    Task<bool> RefreshAsync(bool force = false, CancellationToken ct = default);
    void Reload();
}
