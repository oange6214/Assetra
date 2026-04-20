namespace Assetra.Core.Models;

/// <summary>
/// Top-level financial classification.
/// Intentionally named FinancialType (not AssetType) to avoid collision
/// with the existing <see cref="AssetType"/> enum (Stock, ETF, Fund, …).
/// </summary>
public enum FinancialType { Asset, Investment, Liability }
