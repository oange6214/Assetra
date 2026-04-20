using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Assetra.Core.Models;
using Assetra.Core.Trading;

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
    [ObservableProperty] private decimal _buyPrice;
    [ObservableProperty] private bool _isActive = true;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _currency = "TWD";
    [ObservableProperty] private decimal _currentPrice;
    [ObservableProperty] private decimal _prevClose;
    [ObservableProperty] private bool _isLoadingPrice;

    // Derived — updated by Refresh()
    [ObservableProperty] private decimal _cost;
    [ObservableProperty] private decimal _marketValue;
    [ObservableProperty] private decimal _pnl;
    [ObservableProperty] private decimal _pnlPercent;
    [ObservableProperty] private bool _isPnlPositive;

    /// <summary>
    /// 賣出時估算費用 = 賣出手續費 + 證交稅 (僅股票/ETF；其他資產類型為 0)。
    /// 不再於 <see cref="Pnl"/> 中扣除（毛損益與券商對齊），
    /// 但被 <see cref="NetValue"/> 使用以顯示「現在賣掉真的能拿到」的金額。
    /// </summary>
    [ObservableProperty] private decimal _estimatedSellFee;

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
    }

    // NetValue 是 MarketValue - EstimatedSellFee 的 computed 屬性，
    // 兩個來源任一變動都要通知 UI。
    partial void OnMarketValueChanged(decimal _) => OnPropertyChanged(nameof(NetValue));
    partial void OnEstimatedSellFeeChanged(decimal _) => OnPropertyChanged(nameof(NetValue));
    partial void OnQuantityChanged(decimal _) => OnPropertyChanged(nameof(QuantityDisplay));

    public void Refresh()
    {
        Cost = BuyPrice * Quantity;
        MarketValue = CurrentPrice * Quantity;

        // 預估賣出費用（僅股票/ETF 適用），作為 NetValue 與 Pnl 的共同輸入。
        EstimatedSellFee = (IsStock && Quantity > 0 && CurrentPrice > 0)
            ? CalcEstimatedSellFee()
            : 0m;

        // 毛損益 = 市值 − 成本（與券商「未實現損益」欄位對齊；EstimatedSellFee 不納入此計算）。
        // 「如果現在賣掉真正到手」用 NetValue（= 市值 − 估算賣出費）欄位呈現。
        Pnl = MarketValue - Cost;
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

    public override string ToString() => $"{Symbol} {Name}";
}
