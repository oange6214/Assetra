using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Portfolio;

/// <summary>
/// PortfolioViewModel partial — 新增投資 (前身為觀察清單) 簡易加入流程。
///
/// 不用整套 AddAssetDialog，因為這裡只要：「symbol + name + 類型 + 幣別」+ Confirm。
/// 走 <see cref="Assetra.Application.Portfolio.Services.IAddAssetWorkflowService.EnsureStockEntryAsync"/>，
/// 建立 PortfolioEntry 但不下單；之後該標的會以「數量 0」出現在持倉清單，使用者
/// 之後再透過買入 dialog 補上交易紀錄即可。
/// </summary>
public partial class PortfolioViewModel
{
    [ObservableProperty] private bool _isAddWatchlistOpen;
    [ObservableProperty] private string _watchlistSymbol = string.Empty;
    [ObservableProperty] private string _watchlistName = string.Empty;
    [ObservableProperty] private string _watchlistError = string.Empty;
    [ObservableProperty] private bool _isWatchlistSubmitting;

    /// <summary>對話框「類型」ComboBox 的選項；對齊 <see cref="AssetType"/> 全部成員。</summary>
    public IReadOnlyList<AssetType> WatchlistAssetTypeOptions { get; } = new[]
    {
        AssetType.Stock,
        AssetType.Etf,
        AssetType.Fund,
        AssetType.Bond,
        AssetType.Crypto,
        AssetType.PreciousMetal,
    };
    [ObservableProperty] private AssetType _watchlistAssetType = AssetType.Stock;

    /// <summary>對話框「幣別」ComboBox 的選項；對齊 multi-currency 支援的 5 個主要 ISO 4217 code。</summary>
    public IReadOnlyList<string> WatchlistCurrencyOptions { get; } = new[]
    {
        "TWD", "USD", "JPY", "HKD", "EUR",
    };
    [ObservableProperty] private string _watchlistCurrency = "TWD";

    [RelayCommand]
    private void OpenAddWatchlistDialog()
    {
        WatchlistSymbol = string.Empty;
        WatchlistName = string.Empty;
        WatchlistError = string.Empty;
        WatchlistAssetType = AssetType.Stock;
        WatchlistCurrency = "TWD";
        IsAddWatchlistOpen = true;
    }

    [RelayCommand]
    private void CloseAddWatchlistDialog() => IsAddWatchlistOpen = false;

    [RelayCommand]
    private async Task ConfirmAddWatchlist()
    {
        WatchlistError = string.Empty;
        if (_addAssetWorkflow is null)
        {
            WatchlistError = "服務未就緒";
            return;
        }
        var symbol = (WatchlistSymbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(symbol))
        {
            WatchlistError = "請輸入代號";
            return;
        }

        try
        {
            IsWatchlistSubmitting = true;
            // 走 Search service 把可能的中文名稱 / 交易所自動補上；失敗就讓 service 內部 fallback
            var resolvedName = string.IsNullOrWhiteSpace(WatchlistName)
                ? _search?.GetName(symbol)
                : WatchlistName.Trim();
            var resolvedExchange = _search?.GetExchange(symbol);

            await _addAssetWorkflow.EnsureStockEntryAsync(
                new EnsureStockEntryRequest(
                    symbol,
                    resolvedExchange,
                    resolvedName,
                    PortfolioGroupId: null,
                    AssetType: WatchlistAssetType,
                    Currency: WatchlistCurrency));

            IsAddWatchlistOpen = false;
            _snackbar?.Success("已加入投資資產");

            // 重新載入持倉以讓新 entry 出現（quantity=0，需取消「隱藏空倉」才看得到）
            await LoadPositionsAsync();
            RebuildTotals();
        }
        catch (Exception ex)
        {
            WatchlistError = ex.Message;
        }
        finally
        {
            IsWatchlistSubmitting = false;
        }
    }
}
