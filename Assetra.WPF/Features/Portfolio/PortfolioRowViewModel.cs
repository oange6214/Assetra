using Assetra.Core.Models;
using Assetra.Core.Trading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Portfolio;

public partial class PortfolioRowViewModel : ObservableObject
{
    public Guid Id { get; init; }

    /// <summary>
    /// All underlying PortfolioEntry IDs for this aggregated row.
    /// A position bought in multiple lots has one entry per lot; they are all tracked here
    /// so that deleting or consolidating the row removes every lot from the database.
    /// Initialised to [Id] by ToRow(); additional IDs are appended on load or new buys.
    /// </summary>
    internal List<Guid> AllEntryIds { get; } = [];

    public string Symbol { get; init; } = string.Empty;
    public string Exchange { get; init; } = string.Empty;
    public DateOnly BuyDate { get; init; }
    public AssetType AssetType { get; init; } = AssetType.Stock;

    /// <summary>
    /// Position-level investment group. Source of truth is PortfolioEntry.PortfolioGroupId.
    /// Trade.PortfolioGroupId is historical transaction context and must not overwrite this.
    /// null means legacy/unassigned or a mixed-group aggregate that needs resolution.
    /// </summary>
    [ObservableProperty] private Guid? _portfolioGroupId;

    /// <summary>
    /// True when multiple active PortfolioEntry lots were aggregated into one visible
    /// row but those lots point at different investment groups.
    /// </summary>
    [ObservableProperty] private bool _hasPortfolioGroupConflict;

    [ObservableProperty] private string _portfolioGroupDisplay = "未分組";

    public bool IsStock => AssetType == AssetType.Stock;

    /// <summary>
    /// 數量顯示文字 — 股票整數（股不可拆分）；其他資產類型保留小數（基金、貴金屬、加密貨幣）。
    /// </summary>
    public string QuantityDisplay => IsStock
        ? ((long)Quantity).ToString("N0")
        : Quantity.ToString("N4");

    /// <summary>True for Taiwan ETFs — affects sell-side transaction tax (0.1% vs 0.3%).</summary>
    public bool IsEtf { get; init; }

    /// <summary>True for Taiwan bond ETFs（尾碼 B）— 賣出證交稅免徵（2026 底前）。</summary>
    public bool IsBondEtf { get; init; }

    /// <summary>
    /// True when a projection snapshot drove Quantity/BuyPrice on this row.
    /// False for freshly-created rows that haven't been reloaded from storage yet
    /// (their Quantity/BuyPrice come from the in-flight buy, not the trade log).
    /// </summary>
    public bool HasProjection { get; init; }

    /// <summary>
    /// Commission discount multiplier (0.1 ~ 1.0; e.g. 0.6 = 6折)。
    /// 來自 <c>AppSettings.CommissionDiscount</c>；PortfolioViewModel 訂閱設定 Changed
    /// 事件後會同步到所有 row 並呼叫 <see cref="Refresh"/> 即時重算淨值／淨損益。
    /// </summary>
    [ObservableProperty] private decimal _commissionDiscount = 1m;

    public string AssetTypeBadgeLabel => AssetType switch
    {
        AssetType.Fund => "基",
        AssetType.PreciousMetal => "金",
        AssetType.Bond => "債",
        AssetType.Crypto => "密",
        _ => "股",
    };

    // Mutable so an in-place save can update them without recreating the row
    [ObservableProperty] private decimal _quantity;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BuyPriceAsMoney))]
    private decimal _buyPrice;
    [ObservableProperty] private bool _isActive = true;

    [ObservableProperty] private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCrossCurrency))]
    [NotifyPropertyChangedFor(nameof(CurrencyDisplay))]
    [NotifyPropertyChangedFor(nameof(BuyPriceAsMoney))]
    [NotifyPropertyChangedFor(nameof(CurrentPriceAsMoney))]
    [NotifyPropertyChangedFor(nameof(MarketValueAsMoney))]
    [NotifyPropertyChangedFor(nameof(CostAsMoney))]
    [NotifyPropertyChangedFor(nameof(NetValueAsMoney))]
    [NotifyPropertyChangedFor(nameof(PnlAsMoney))]
    [NotifyPropertyChangedFor(nameof(EstimatedSellFeeAsMoney))]
    private string _currency = "TWD";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPriceAsMoney))]
    private decimal _currentPrice;

    [ObservableProperty] private decimal _prevClose;
    [ObservableProperty] private bool _isLoadingPrice;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowQuoteProviderState))]
    private bool _isQuoteStale;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowQuoteProviderState))]
    [NotifyPropertyChangedFor(nameof(HasQuoteProviderStateMessage))]
    private string _quoteProviderStateMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCrossCurrency))]
    [NotifyPropertyChangedFor(nameof(BuyPriceBaseAsMoney))]
    [NotifyPropertyChangedFor(nameof(CurrentPriceBaseAsMoney))]
    [NotifyPropertyChangedFor(nameof(CostBaseAsMoney))]
    [NotifyPropertyChangedFor(nameof(MarketValueBaseAsMoney))]
    [NotifyPropertyChangedFor(nameof(NetValueBaseAsMoney))]
    [NotifyPropertyChangedFor(nameof(PnlBaseAsMoney))]
    [NotifyPropertyChangedFor(nameof(EstimatedSellFeeBaseAsMoney))]
    private string _baseCurrency = "TWD";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BuyPriceBaseAsMoney))]
    private decimal _buyPriceBase;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPriceBaseAsMoney))]
    private decimal _currentPriceBase;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CostBaseAsMoney))]
    private decimal _costBase;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarketValueBaseAsMoney))]
    private decimal _marketValueBase;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetValueBaseAsMoney))]
    private decimal _netValueBase;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PnlBaseAsMoney))]
    private decimal _pnlBase;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstimatedSellFeeBaseAsMoney))]
    private decimal _estimatedSellFeeBase;

    // Derived — updated by Refresh()
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CostAsMoney))]
    private decimal _cost;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarketValueAsMoney))]
    private decimal _marketValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PnlAsMoney))]
    private decimal _pnl;

    [ObservableProperty] private decimal _pnlPercent;
    [ObservableProperty] private bool _isPnlPositive;

    /// <summary>
    /// M1 — currency-tagged accessors. Use these for any cross-row aggregation
    /// that must respect currency boundaries (concentration, multi-currency
    /// portfolio summary). Existing decimal properties stay primary for XAML.
    /// </summary>
    public Money BuyPriceAsMoney => new(BuyPrice, NormalizedCurrency);
    public Money CurrentPriceAsMoney => new(CurrentPrice, NormalizedCurrency);
    public Money MarketValueAsMoney => new(MarketValue, NormalizedCurrency);
    public Money CostAsMoney => new(Cost, NormalizedCurrency);
    public Money NetValueAsMoney => new(NetValue, NormalizedCurrency);
    public Money PnlAsMoney => new(Pnl, NormalizedCurrency);
    public Money EstimatedSellFeeAsMoney => new(EstimatedSellFee, NormalizedCurrency);

    public Money BuyPriceBaseAsMoney => new(BuyPriceBase, NormalizedBaseCurrency);
    public Money CurrentPriceBaseAsMoney => new(CurrentPriceBase, NormalizedBaseCurrency);
    public Money CostBaseAsMoney => new(CostBase, NormalizedBaseCurrency);
    public Money MarketValueBaseAsMoney => new(MarketValueBase, NormalizedBaseCurrency);
    public Money NetValueBaseAsMoney => new(NetValueBase, NormalizedBaseCurrency);
    public Money PnlBaseAsMoney => new(PnlBase, NormalizedBaseCurrency);
    public Money EstimatedSellFeeBaseAsMoney => new(EstimatedSellFeeBase, NormalizedBaseCurrency);

    public bool IsCrossCurrency =>
        !string.Equals(NormalizedCurrency, NormalizedBaseCurrency, StringComparison.OrdinalIgnoreCase);

    public string CurrencyDisplay => NormalizedCurrency;

    private string NormalizedCurrency => string.IsNullOrWhiteSpace(Currency) ? "TWD" : Currency.Trim().ToUpperInvariant();
    private string NormalizedBaseCurrency => string.IsNullOrWhiteSpace(BaseCurrency) ? "TWD" : BaseCurrency.Trim().ToUpperInvariant();

    public bool HasQuoteProviderStateMessage => !string.IsNullOrWhiteSpace(QuoteProviderStateMessage);
    public bool ShowQuoteProviderState => IsQuoteStale || HasQuoteProviderStateMessage;

    /// <summary>
    /// 賣出時估算費用 = 賣出手續費 + 證交稅 (僅股票/ETF；其他資產類型為 0)。
    /// 會被 <see cref="NetValue"/> 與 <see cref="Pnl"/> 使用，以呈現
    /// 「現在賣掉真的能拿到」的淨值與淨損益。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstimatedSellFeeAsMoney))]
    private decimal _estimatedSellFee;

    /// <summary>
    /// 淨值 = 市值 − 預估賣出手續費 − 證交稅
    /// 證交稅依 <see cref="IsEtf"/> 判定：ETF 0.1%、一般股票 0.3%。
    /// 其他資產類型 (Fund / Metal / Bond / Crypto) 的 EstimatedSellFee = 0 → 淨值 = 市值。
    /// </summary>
    public decimal NetValue => MarketValue - EstimatedSellFee;

    // 日漲跌 — only valid after first quote with PrevClose arrives
    [ObservableProperty] private decimal _dayChange;
    [ObservableProperty] private decimal _dayChangePercent;
    [ObservableProperty] private bool _isDayChangePositive;

    /// <summary>
    /// 30 天收盤價序列 — sparkline 欄用。
    /// 三種狀態的 UI 對應：
    ///   - SparklineState = Loading：cell 顯示「…」
    ///   - SparklineState = Loaded（SparklinePoints.Length ≥ 2）：渲染 mini line chart
    ///   - SparklineState = Unavailable（API 失敗 / 無歷史資料）：cell 顯示「—」
    /// 由 PortfolioViewModel.LoadSparklinesAsync 從 CachedStockHistoryProvider 填值；
    /// 快取存於 equity_ohlc_cache 表，每符號每天最多打外部 API 一次。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSparkline))]
    [NotifyPropertyChangedFor(nameof(IsSparklinePositive))]
    private double[]? _sparklinePoints;

    /// <summary>0=Loading（預設）、1=Loaded、2=Unavailable。XAML 用 DataTrigger 三態顯示。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSparklineLoading))]
    [NotifyPropertyChangedFor(nameof(IsSparklineUnavailable))]
    private int _sparklineState; // 0 by default = Loading

    public bool HasSparkline => SparklineState == 1 && SparklinePoints is { Length: >= 2 };
    public bool IsSparklineLoading => SparklineState == 0;
    public bool IsSparklineUnavailable => SparklineState == 2;

    /// <summary>True 當 sparkline 末值 ≥ 首值（決定 line 顏色，紅漲綠跌）。</summary>
    public bool IsSparklinePositive =>
        SparklinePoints is { Length: >= 2 } pts && pts[^1] >= pts[0];

    /// <summary>
    /// 本持倉佔投資組合總淨值的百分比（0–100）。由 PortfolioViewModel 在總計
    /// 重算時統一寫入，row 自身不計算（避免迴圈依賴）。
    /// </summary>
    [ObservableProperty] private decimal _percentOfPortfolio;


    /// <summary>
    /// 貨幣切換時由 PortfolioViewModel 呼叫，
    /// 強制所有金額屬性重新通知，讓 XAML CurrencyConverter 重新格式化。
    /// </summary>
    public void NotifyCurrencyChanged()
    {
        OnPropertyChanged(nameof(Cost));
        OnPropertyChanged(nameof(BuyPrice));
        OnPropertyChanged(nameof(CurrentPrice));
        OnPropertyChanged(nameof(Pnl));
        OnPropertyChanged(nameof(DayChange));
        OnPropertyChanged(nameof(MarketValue));
        OnPropertyChanged(nameof(EstimatedSellFee));
        OnPropertyChanged(nameof(NetValue));
        OnPropertyChanged(nameof(BuyPriceAsMoney));
        OnPropertyChanged(nameof(CurrentPriceAsMoney));
        OnPropertyChanged(nameof(CostAsMoney));
        OnPropertyChanged(nameof(MarketValueAsMoney));
        OnPropertyChanged(nameof(NetValueAsMoney));
        OnPropertyChanged(nameof(PnlAsMoney));
        OnPropertyChanged(nameof(EstimatedSellFeeAsMoney));
        OnPropertyChanged(nameof(BuyPriceBaseAsMoney));
        OnPropertyChanged(nameof(CurrentPriceBaseAsMoney));
        OnPropertyChanged(nameof(CostBaseAsMoney));
        OnPropertyChanged(nameof(MarketValueBaseAsMoney));
        OnPropertyChanged(nameof(NetValueBaseAsMoney));
        OnPropertyChanged(nameof(PnlBaseAsMoney));
        OnPropertyChanged(nameof(EstimatedSellFeeBaseAsMoney));
        OnPropertyChanged(nameof(IsCrossCurrency));
        OnPropertyChanged(nameof(CurrencyDisplay));
    }

    // NetValue 是 MarketValue - EstimatedSellFee 的 computed 屬性，
    // 兩個來源任一變動都要通知 UI。
    partial void OnMarketValueChanged(decimal _)
    {
        OnPropertyChanged(nameof(NetValue));
        OnPropertyChanged(nameof(NetValueAsMoney));
    }

    partial void OnEstimatedSellFeeChanged(decimal _)
    {
        OnPropertyChanged(nameof(NetValue));
        OnPropertyChanged(nameof(NetValueAsMoney));
    }
    partial void OnQuantityChanged(decimal _) => OnPropertyChanged(nameof(QuantityDisplay));

    public void ApplyBaseValuation(string baseCurrency, IReadOnlyDictionary<string, decimal>? rates)
    {
        BaseCurrency = string.IsNullOrWhiteSpace(baseCurrency) ? "TWD" : baseCurrency;
        BuyPriceBase = ConvertToBase(BuyPrice, rates);
        CurrentPriceBase = ConvertToBase(CurrentPrice, rates);
        CostBase = ConvertToBase(Cost, rates);
        MarketValueBase = ConvertToBase(MarketValue, rates);
        NetValueBase = ConvertToBase(NetValue, rates);
        PnlBase = ConvertToBase(Pnl, rates);
        EstimatedSellFeeBase = ConvertToBase(EstimatedSellFee, rates);
        OnPropertyChanged(nameof(IsCrossCurrency));
    }

    private decimal ConvertToBase(decimal amount, IReadOnlyDictionary<string, decimal>? rates) =>
        CurrencyValuation.ConvertToBase(amount, NormalizedCurrency, NormalizedBaseCurrency, rates);

    public void Refresh()
    {
        Cost = BuyPrice * Quantity;
        MarketValue = CurrentPrice * Quantity;

        // 預估賣出費用（僅股票/ETF 適用），作為 NetValue 與 Pnl 的共同輸入。
        EstimatedSellFee = (UsesTaiwanSellFee && Quantity > 0 && CurrentPrice > 0)
            ? CalcEstimatedSellFee()
            : 0m;

        // 淨損益 = 淨值 − 成本。成本已含買入手續費，淨值已扣預估賣出手續費與證交稅。
        Pnl = NetValue - Cost;
        PnlPercent = Cost > 0 ? Pnl / Cost * 100m : 0m;
        IsPnlPositive = Pnl >= 0;

        if (PrevClose > 0)
        {
            DayChange = (CurrentPrice - PrevClose) * Quantity;
            DayChangePercent = (CurrentPrice - PrevClose) / PrevClose * 100m;
            IsDayChangePositive = DayChange >= 0;
        }
    }

    private decimal CalcEstimatedSellFee()
    {
        var fee = TaiwanTradeFeeCalculator.CalcSell(
            CurrentPrice, (int)Quantity, CommissionDiscount, IsEtf, IsBondEtf);
        return fee.Commission + fee.TransactionTax;
    }

    private bool UsesTaiwanSellFee =>
        IsStock &&
        (string.IsNullOrWhiteSpace(Exchange) ||
         string.Equals(Exchange, "TWSE", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(Exchange, "TPEX", StringComparison.OrdinalIgnoreCase));

    public override string ToString() => $"{Symbol} {Name}";
}
