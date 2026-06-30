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

    /// <summary>
    /// 非阻擋式「即時報價可用性」提示（advisory）。代號變更與 Confirm 時重算；
    /// 成功加入後清空。永遠不阻擋加入流程（與 <see cref="WatchlistError"/> 區隔）。
    /// 內容例如：美股未填 Twelve Data 金鑰 → 將使用 Yahoo 後備；代號目錄查無此代號；
    /// 美股代號目錄尚未下載。null/空字串時 XAML 隱藏。
    /// </summary>
    [ObservableProperty] private string? _watchlistNotice;

    /// <summary>代號自動完成建議（台股＋美股，主來源 ISymbolDirectory、fallback 台股 CSV）。</summary>
    [ObservableProperty] private IReadOnlyList<StockSearchResult> _watchlistSuggestions = [];

    /// <summary>代號輸入框變動時即時重算提示（cheap，純記憶體判定）＋更新自動完成建議。</summary>
    partial void OnWatchlistSymbolChanged(string value)
    {
        RefreshWatchlistNotice();
        var q = (value ?? string.Empty).Trim();
        WatchlistSuggestions = string.IsNullOrWhiteSpace(q)
            ? []
            : (_symbolDirectory?.Search(q) ?? _search.Search(q) ?? []).Take(8).ToList();
    }

    /// <summary>點選建議：帶入代號、名稱、幣別（若在選項內）、類型（ETF/股），並清空建議清單。</summary>
    [RelayCommand]
    private void SelectWatchlistSuggestion(StockSearchResult? r)
    {
        if (r is null)
            return;
        WatchlistAssetType = r.IsEtf ? AssetType.Etf : AssetType.Stock;
        if (!string.IsNullOrWhiteSpace(r.Name))
            WatchlistName = r.Name;
        if (WatchlistCurrencyOptions.Contains(r.Currency))
            WatchlistCurrency = r.Currency;
        WatchlistSymbol = r.Symbol;   // 設 symbol 會重觸 OnWatchlistSymbolChanged 重查；下一行再清掉建議
        WatchlistSuggestions = [];
    }

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
        WatchlistNotice = null;
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
        // Advisory：在送出前再算一次（涵蓋使用者未觸發 change 就直接按確認的情況）。
        RefreshWatchlistNotice();
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
            WatchlistNotice = null;
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

    /// <summary>
    /// 重算 <see cref="WatchlistNotice"/>（advisory，純記憶體判定）。規則：
    /// <list type="bullet">
    ///   <item>空代號 → 清空提示。</item>
    ///   <item>美股代號 + 未填 Twelve Data 金鑰 → 提示將用 Yahoo 後備。</item>
    ///   <item>美股代號 + 代號目錄尚未下載 → 提示先更新目錄（此時不再追加「找不到」，因目錄無法判定）。</item>
    ///   <item>代號目錄可用但查無此代號 → 提示確認拼寫。</item>
    /// </list>
    /// 多條同時成立時以空白合併，保持精簡。
    /// </summary>
    private void RefreshWatchlistNotice()
    {
        var symbol = (WatchlistSymbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(symbol))
        {
            WatchlistNotice = null;
            return;
        }

        // 交易所判定：先用 search service 的精確對照；查無對照時用 workflow 的 InferExchange
        // 推導（同 EnsureStockEntry 的 fallback），再以 StockExchangeRegistry 判定是否為美股。
        var exchange = _search?.GetExchange(symbol);
        var isUs = IsUsExchange(exchange)
            || (string.IsNullOrEmpty(exchange) && IsUsExchange(_addAssetWorkflow?.InferExchange(symbol)));

        // 代號 / 目錄就緒狀態（純讀取）。workflow 為 null（理論上不會）時退化為「無提示」。
        var readiness = _addAssetWorkflow?.CheckWatchlistSymbol(symbol, exchange);

        var parts = new List<string>(2);

        // 美股未填金鑰 → 後備來源提示。金鑰是否存在以 AppSettings 為準（與 ShowQuoteProviderNotice 一致）。
        var hasTwelveDataKey =
            !string.IsNullOrWhiteSpace(_settingsService?.Current.TwelveDataApiKey);
        if (isUs && !hasTwelveDataKey)
        {
            parts.Add(L("Portfolio.Watchlist.Notice.UsNoKey",
                "美股即時報價建議在『設定 → 資料來源』填入 Twelve Data API 金鑰；目前將使用 Yahoo 後備來源（可能延遲/受限）。"));
        }

        if (isUs && readiness is { UsDirectoryReady: false })
        {
            // 目錄尚未下載：無法判斷代號是否存在，提示更新目錄，且不再追加「找不到」。
            parts.Add(L("Portfolio.Watchlist.Notice.DirectoryEmpty",
                "美股代號目錄尚未下載，請至『設定 → 資料來源』更新後再試。"));
        }
        else if (readiness is { IsResolved: false })
        {
            // 目錄可用（或非美股，TW 目錄恆有資料）但查無此代號。
            parts.Add(L("Portfolio.Watchlist.Notice.SymbolNotFound",
                "找不到此代號，請確認拼寫；若確定正確，可能此商品暫不支援。"));
        }

        WatchlistNotice = parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static bool IsUsExchange(string? exchange) =>
        string.Equals(
            StockExchangeRegistry.TryGet(exchange)?.Country,
            "US",
            StringComparison.OrdinalIgnoreCase);
}
