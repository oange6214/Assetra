namespace Assetra.Core.Interfaces;

/// <summary>
/// Fetches real-time cryptocurrency prices from a free public API.
/// </summary>
public interface ICryptoService
{
    /// <summary>
    /// Returns a symbol → TWD price map for the requested symbols.
    /// Unknown or unresolvable symbols are omitted from the result.
    /// </summary>
    Task<IReadOnlyDictionary<string, decimal>> GetPricesTwdAsync(IReadOnlyList<string> symbols);
}
