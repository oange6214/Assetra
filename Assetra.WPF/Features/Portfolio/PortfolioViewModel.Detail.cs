using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Portfolio;

/// <summary>
/// PortfolioViewModel partial — detail-panel state (selected row, computed stats,
/// detail tab, close/remove commands) for Cash, Liability, and Position rows.
/// </summary>
public partial class PortfolioViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedCashRow))]
    [NotifyPropertyChangedFor(nameof(SelectedCashTrades))]
    [NotifyPropertyChangedFor(nameof(SelectedCashTotalDeposits))]
    [NotifyPropertyChangedFor(nameof(SelectedCashTotalWithdrawals))]
    private CashAccountRowViewModel? _selectedCashRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedLiabilityRow))]
    [NotifyPropertyChangedFor(nameof(SelectedLiabilityTrades))]
    [NotifyPropertyChangedFor(nameof(SelectedLiabilityTotalBorrows))]
    [NotifyPropertyChangedFor(nameof(SelectedLiabilityTotalRepays))]
    private LiabilityRowViewModel? _selectedLiabilityRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPositionRow))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionTrades))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionDividendIncome))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionCapitalGain))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionCapitalGainIsPositive))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionRealizedTotal))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionRealizedTotalIsPositive))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionTradeAvgPrice))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionTradeAvgPriceAsMoney))]
    [NotifyPropertyChangedFor(nameof(HasSelectedPositionRealized))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionRoi1YDisplay))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionRoi3YDisplay))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionRoiCumDisplay))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionXirr1YDisplay))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionXirr3YDisplay))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionXirrCumDisplay))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionRoi1YIsPositive))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionRoi3YIsPositive))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionRoiCumIsPositive))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionXirr1YIsPositive))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionXirr3YIsPositive))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionXirrCumIsPositive))]
    private PortfolioRowViewModel? _selectedPositionRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPortfolioGroupDetail))]
    private PortfolioGroupDetailViewModel? _selectedPortfolioGroupDetail;

    /// <summary>
    /// 是否有任何已實現損益資料（賣出價差或股息收入）。
    /// </summary>
    public bool HasSelectedPositionRealized =>
        SelectedPositionRealizedTotal != 0m || SelectedPositionDividendIncome > 0m;

    public bool HasSelectedCashRow => SelectedCashRow is not null;
    public bool HasSelectedLiabilityRow => SelectedLiabilityRow is not null;
    public bool HasSelectedPositionRow => SelectedPositionRow is not null;
    public bool HasSelectedPortfolioGroupDetail => SelectedPortfolioGroupDetail is not null;

    /// <summary>
    /// Detail-panel active tab ("overview" or "trades"). Shared between the Cash and
    /// Liability panels — each panel only shows at most one at a time so a single
    /// property is sufficient; resets to "overview" whenever the selected row changes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOverviewTab))]
    [NotifyPropertyChangedFor(nameof(IsDetailTradesTab))]
    [NotifyPropertyChangedFor(nameof(IsDetailScheduleTab))]
    private string _detailTab = "overview";

    public bool IsDetailOverviewTab => DetailTab == "overview";
    public bool IsDetailTradesTab => DetailTab == "trades";
    public bool IsDetailScheduleTab => DetailTab == "schedule";

    partial void OnSelectedCashRowChanged(CashAccountRowViewModel? _) => DetailTab = "overview";
    partial void OnSelectedLiabilityRowChanged(LiabilityRowViewModel? row)
    {
        DetailTab = "overview";
        if (row is { IsLoan: true, IsScheduleLoaded: false })
            _ = Loan.LoadLoanScheduleAsync(row);
    }
    partial void OnSelectedPositionRowChanged(PortfolioRowViewModel? row)
    {
        DetailTab = "overview";
        OpenSelectedPositionGroupDetailCommand.NotifyCanExecuteChanged();
        // P4.5 — fire chart reload (fire-and-forget; the load handles its own
        // cancellation so racing selections collapse cleanly).
        _ = LoadAssetChartAsync();
    }

    private PortfolioGroupDetailViewModel BuildPortfolioGroupDetail(Guid groupId)
    {
        var holdings = Positions
            .Where(row => !row.HasPortfolioGroupConflict &&
                          (row.PortfolioGroupId ?? PortfolioGroup.DefaultId) == groupId)
            .OrderByDescending(row => row.MarketValueBase != 0m ? row.MarketValueBase : row.MarketValue)
            .ThenBy(row => row.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var groupName = GroupCatalog?.FindById(groupId)?.Name
            ?? (groupId == PortfolioGroup.DefaultId
                ? L("Portfolio.Group.Ungrouped", "未分組")
                : L("Portfolio.Group.Unknown", "未知群組"));

        var trades = BuildPortfolioGroupTrades(groupId, holdings);

        return new PortfolioGroupDetailViewModel(groupId, groupName, ResolveBaseCurrency(), holdings, trades);
    }

    private IReadOnlyList<TradeRowViewModel> BuildPortfolioGroupTrades(
        Guid groupId,
        IReadOnlyList<PortfolioRowViewModel> holdings)
    {
        if (holdings.Count == 0)
            return [];

        var entryIds = holdings
            .SelectMany(row => row.AllEntryIds)
            .ToHashSet();
        var symbols = holdings
            .Select(row => row.Symbol)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Trades
            .Where(trade => IsInvestmentTrade(trade) &&
                            IsCurrentGroupMemberTrade(trade, groupId, entryIds, symbols))
            .GroupBy(trade => trade.Id)
            .Select(group => group.First())
            .OrderByDescending(trade => trade.TradeDate)
            .ThenBy(trade => trade.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsCurrentGroupMemberTrade(
        TradeRowViewModel trade,
        Guid groupId,
        ISet<Guid> entryIds,
        ISet<string> symbols)
    {
        if (trade.PortfolioEntryId is { } entryId && entryIds.Contains(entryId))
            return true;

        if (!symbols.Contains(trade.Symbol))
            return false;

        return (trade.PortfolioGroupId ?? PortfolioGroup.DefaultId) == groupId ||
               trade.PortfolioEntryId is null;
    }

    private static bool IsInvestmentTrade(TradeRowViewModel trade) =>
        trade.Type is TradeType.Buy or TradeType.Sell or TradeType.CashDividend or TradeType.StockDividend;

    private void RefreshSelectedPortfolioGroupDetail()
    {
        if (SelectedPortfolioGroupDetail is { } detail)
            SelectedPortfolioGroupDetail = BuildPortfolioGroupDetail(detail.GroupId);
    }

    // Cash account stats + filtered trades
    public IEnumerable<TradeRowViewModel> SelectedCashTrades =>
        SelectedCashRow is { } r
            ? Trades
                .Where(t => t.CashAccountId == r.Id ||
                            (t.IsTransfer && t.ToCashAccountId == r.Id))
                // 目標帳戶（收款方）的轉帳改以「收款視角」呈現（金額為正）；來源帳戶維持流出 −。
                .Select(t => t.IsTransfer && t.ToCashAccountId == r.Id && t.CashAccountId != r.Id
                    ? t.AsIncomingTransferView()
                    : t)
                .OrderByDescending(t => t.TradeDate)
                .ToList()
            : [];

    public decimal SelectedCashTotalDeposits =>
        SelectedCashRow is { } r
            ? Trades.Where(t => t.CashAccountId == r.Id &&
                                (t.Type == TradeType.Deposit ||
                                 t.Type == TradeType.Income ||
                                 t.Type == TradeType.CashDividend ||
                                 t.Type == TradeType.LoanBorrow ||
                                 t.Type == TradeType.Sell))
                    .Sum(t => Math.Abs(t.CashAmount ?? 0))
            : 0m;

    public decimal SelectedCashTotalWithdrawals =>
        SelectedCashRow is { } r
            ? Trades.Where(t => t.CashAccountId == r.Id &&
                                (t.Type == TradeType.Withdrawal ||
                                 t.Type == TradeType.Buy ||
                                 t.Type == TradeType.LoanRepay ||
                                 t.Type == TradeType.CreditCardPayment))
                    .Sum(t => Math.Abs(t.CashAmount ?? 0))
            : 0m;

    // Liability stats + filtered trades
    public IEnumerable<TradeRowViewModel> SelectedLiabilityTrades =>
        SelectedLiabilityRow is { } r
            ? Trades.Where(t => IsTradeForLiability(t, r))
                    .OrderByDescending(t => t.TradeDate)
            : [];

    public decimal SelectedLiabilityTotalBorrows =>
        SelectedLiabilityRow is { } r
            ? Trades.Where(t =>
                    (t.Type == TradeType.LoanBorrow && t.LoanLabel == r.Label) ||
                    (t.Type == TradeType.CreditCardCharge && LiabilityMatches(t, r)))
                    .Sum(t => t.CashAmount ?? 0)
            : 0m;

    public decimal SelectedLiabilityTotalRepays =>
        SelectedLiabilityRow is { } r
            ? Trades.Where(t =>
                    (t.Type == TradeType.LoanRepay && t.LoanLabel == r.Label) ||
                    (t.Type == TradeType.CreditCardPayment && LiabilityMatches(t, r)))
                    .Sum(t => t.CashAmount ?? 0)
            : 0m;

    private static bool IsTradeForLiability(TradeRowViewModel trade, LiabilityRowViewModel liability) =>
        trade.Type switch
        {
            TradeType.LoanBorrow or TradeType.LoanRepay => trade.LoanLabel == liability.Label,
            TradeType.CreditCardCharge or TradeType.CreditCardPayment => LiabilityMatches(trade, liability),
            _ => false,
        };

    private static bool LiabilityMatches(TradeRowViewModel trade, LiabilityRowViewModel liability) =>
        liability.AssetId.HasValue && trade.LiabilityAssetId == liability.AssetId;

    // Investment position stats + filtered trades
    public IEnumerable<TradeRowViewModel> SelectedPositionTrades =>
        SelectedPositionRow is { } r
            ? Trades.Where(t => string.Equals(t.Symbol, r.Symbol, StringComparison.OrdinalIgnoreCase) &&
                                (t.Type == TradeType.Buy || t.Type == TradeType.Sell ||
                                 t.Type == TradeType.CashDividend || t.Type == TradeType.StockDividend))
                    .OrderByDescending(t => t.TradeDate)
            : [];

    /// <summary>
    /// 成交均價 — 以 Buy 交易紀錄的股數加權平均，**不含買入手續費**。
    /// </summary>
    public decimal SelectedPositionTradeAvgPrice
    {
        get
        {
            if (SelectedPositionRow is not { } r)
                return 0m;
            var buys = Trades
                .Where(t => t.IsBuy && string.Equals(t.Symbol, r.Symbol, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (buys.Count == 0)
                return 0m;
            var totalQty = buys.Sum(t => (decimal)t.Quantity);
            var totalGross = buys.Sum(t => t.Price * t.Quantity);
            return totalQty > 0 ? totalGross / totalQty : 0m;
        }
    }

    public Money SelectedPositionTradeAvgPriceAsMoney =>
        new(SelectedPositionTradeAvgPrice, SelectedPositionRow?.Currency ?? "TWD");

    /// <summary>Sum of all CashDividend trades for the selected position (gross dividend income).</summary>
    public decimal SelectedPositionDividendIncome =>
        SelectedPositionRow is { } r
            ? Trades.Where(t => string.Equals(t.Symbol, r.Symbol, StringComparison.OrdinalIgnoreCase) &&
                                t.Type == TradeType.CashDividend)
                    .Sum(t => t.CashAmount ?? 0)
            : 0m;

    /// <summary>
    /// 已實現資本利得 — 對該標的所有 Sell trade 的 <c>RealizedPnl</c> 加總。
    /// 來源為 Trade row 寫入時計算的 FIFO realized P&amp;L（含手續費與證交稅）；
    /// legacy / 缺值 trade 視為 0，不會中斷加總。
    /// </summary>
    public decimal SelectedPositionCapitalGain =>
        SelectedPositionRow is { } r
            ? Trades.Where(t => t.Type == TradeType.Sell &&
                                string.Equals(t.Symbol, r.Symbol, StringComparison.OrdinalIgnoreCase))
                    .Sum(t => t.RealizedPnl ?? 0m)
            : 0m;

    public decimal SelectedPositionRealizedTotal =>
        SelectedPositionDividendIncome + SelectedPositionCapitalGain;

    /// <summary>P4.2 — Capital gain 可能為負（虧損賣出），UI 依符號染色用。</summary>
    public bool SelectedPositionCapitalGainIsPositive => SelectedPositionCapitalGain >= 0m;

    /// <summary>P4.2 — Realized total 可能為負（虧損 &gt; 股息），UI 依符號染色用。</summary>
    public bool SelectedPositionRealizedTotalIsPositive => SelectedPositionRealizedTotal >= 0m;

    // ── P4.1 — Asset KPI matrix (ROI / XIRR × 1Y / 3Y / 累積) ─────────────
    // 從 SelectedPositionTrades 重播交易，計算單一資產的視窗 ROI 與 XIRR。
    //   windowStart = null → 累積（自第一筆 trade 至今）
    //   windowStart 早於第一筆 trade → 視為無足夠歷史 (1Y/3Y 顯示 "—")
    //   XIRR 透過 _xirrCalculator（null 或無法收斂時回傳 null → UI 顯示 "—"）
    // 開倉成本基底 (openingCost) 透過 moving-average basis 重播，Sell 按比例
    // 折減成本，StockDividend 增加股數但成本不變，CashDividend 不影響基底。

    private (decimal? roi, decimal? xirr) ComputeAssetReturn(DateOnly? windowStart)
    {
        if (SelectedPositionRow is not { } row)
            return (null, null);

        var ordered = Trades
            .Where(t => string.Equals(t.Symbol, row.Symbol, StringComparison.OrdinalIgnoreCase) &&
                        (t.Type == TradeType.Buy || t.Type == TradeType.Sell ||
                         t.Type == TradeType.CashDividend || t.Type == TradeType.StockDividend))
            .OrderBy(t => t.TradeDate)
            .ToList();
        if (ordered.Count == 0)
            return (null, null);

        var firstTradeDate = DateOnly.FromDateTime(ordered[0].TradeDate);
        // 1Y / 3Y 視窗：若第一筆交易晚於視窗起點，視為無足夠歷史 → "—"
        if (windowStart is { } ws && ws < firstTradeDate)
            return (null, null);

        var actualStart = windowStart ?? firstTradeDate;

        // 重播視窗前的交易 → openingQty / openingCost（moving-average basis）
        decimal openingQty = 0m, openingCost = 0m;
        foreach (var t in ordered)
        {
            var date = DateOnly.FromDateTime(t.TradeDate);
            if (date >= actualStart)
                break;
            switch (t.Type)
            {
                case TradeType.Buy:
                    openingQty += t.Quantity;
                    openingCost += t.Price * t.Quantity + (t.Commission ?? 0m);
                    break;
                case TradeType.Sell when openingQty > 0:
                    var sellQty = Math.Min((decimal)t.Quantity, openingQty);
                    var perShare = openingCost / openingQty;
                    openingCost -= perShare * sellQty;
                    openingQty -= sellQty;
                    break;
                case TradeType.StockDividend:
                    openingQty += t.Quantity;
                    break;
            }
        }

        var windowTrades = ordered.Where(t => DateOnly.FromDateTime(t.TradeDate) >= actualStart).ToList();
        var currentValue = row.MarketValue;
        var today = DateOnly.FromDateTime(DateTime.Today);

        // ROI = (currentValue + 視窗內收回 − 開倉基底 − 視窗內投入) / (開倉基底 + 視窗內投入)
        decimal invested = openingCost;
        decimal returned = currentValue;
        foreach (var t in windowTrades)
        {
            switch (t.Type)
            {
                case TradeType.Buy:
                    invested += t.Price * t.Quantity + (t.Commission ?? 0m);
                    break;
                case TradeType.Sell:
                    returned += t.Price * t.Quantity - (t.Commission ?? 0m);
                    break;
                case TradeType.CashDividend:
                    returned += t.CashAmount ?? 0m;
                    break;
            }
        }
        decimal? roi = invested > 0 ? (returned - invested) / invested : null;

        // XIRR — 開倉視為一筆 −openingCost 的買入；視窗內 Buy/Sell/CashDividend 為實際現金流；
        //        加上今天 +currentValue 作為合成「賣出」cash flow。
        decimal? xirr = null;
        if (_xirrCalculator is not null)
        {
            var flows = new List<Assetra.Core.Models.Analysis.CashFlow>();
            if (openingQty > 0 && openingCost > 0)
                flows.Add(new(actualStart, -openingCost));
            foreach (var t in windowTrades)
            {
                var date = DateOnly.FromDateTime(t.TradeDate);
                switch (t.Type)
                {
                    case TradeType.Buy:
                        flows.Add(new(date, -(t.Price * t.Quantity + (t.Commission ?? 0m))));
                        break;
                    case TradeType.Sell:
                        flows.Add(new(date, +(t.Price * t.Quantity - (t.Commission ?? 0m))));
                        break;
                    case TradeType.CashDividend:
                        flows.Add(new(date, +(t.CashAmount ?? 0m)));
                        break;
                }
            }
            if (currentValue > 0)
                flows.Add(new(today, +currentValue));
            if (flows.Count >= 2 && flows.Any(f => f.Amount < 0) && flows.Any(f => f.Amount > 0))
                xirr = _xirrCalculator.Compute(flows);
        }
        return (roi, xirr);
    }

    private (decimal? roi, decimal? xirr) Returns1Y =>
        ComputeAssetReturn(DateOnly.FromDateTime(DateTime.Today.AddYears(-1)));
    private (decimal? roi, decimal? xirr) Returns3Y =>
        ComputeAssetReturn(DateOnly.FromDateTime(DateTime.Today.AddYears(-3)));
    private (decimal? roi, decimal? xirr) ReturnsCum => ComputeAssetReturn(null);

    public string SelectedPositionRoi1YDisplay => FormatReturnPercent(Returns1Y.roi);
    public string SelectedPositionRoi3YDisplay => FormatReturnPercent(Returns3Y.roi);
    public string SelectedPositionRoiCumDisplay => FormatReturnPercent(ReturnsCum.roi);
    public string SelectedPositionXirr1YDisplay => FormatReturnPercent(Returns1Y.xirr);
    public string SelectedPositionXirr3YDisplay => FormatReturnPercent(Returns3Y.xirr);
    public string SelectedPositionXirrCumDisplay => FormatReturnPercent(ReturnsCum.xirr);

    // IsPositive 預設 true（包括 null），UI 只用於上漲色；null 時顯示「—」自然中性。
    public bool SelectedPositionRoi1YIsPositive => (Returns1Y.roi ?? 0m) >= 0m;
    public bool SelectedPositionRoi3YIsPositive => (Returns3Y.roi ?? 0m) >= 0m;
    public bool SelectedPositionRoiCumIsPositive => (ReturnsCum.roi ?? 0m) >= 0m;
    public bool SelectedPositionXirr1YIsPositive => (Returns1Y.xirr ?? 0m) >= 0m;
    public bool SelectedPositionXirr3YIsPositive => (Returns3Y.xirr ?? 0m) >= 0m;
    public bool SelectedPositionXirrCumIsPositive => (ReturnsCum.xirr ?? 0m) >= 0m;

    private static string FormatReturnPercent(decimal? value) =>
        value is { } v ? (v >= 0m ? "+" : "") + (v * 100m).ToString("F1") + "%" : "—";

    private void NotifyTradeDependentDetailPropertiesChanged()
    {
        OnPropertyChanged(nameof(SelectedCashTrades));
        OnPropertyChanged(nameof(SelectedCashTotalDeposits));
        OnPropertyChanged(nameof(SelectedCashTotalWithdrawals));
        OnPropertyChanged(nameof(SelectedLiabilityTrades));
        OnPropertyChanged(nameof(SelectedLiabilityTotalBorrows));
        OnPropertyChanged(nameof(SelectedLiabilityTotalRepays));
        OnPropertyChanged(nameof(SelectedPositionTrades));
        OnPropertyChanged(nameof(SelectedPositionDividendIncome));
        OnPropertyChanged(nameof(SelectedPositionCapitalGain));
        OnPropertyChanged(nameof(SelectedPositionCapitalGainIsPositive));
        OnPropertyChanged(nameof(SelectedPositionRealizedTotal));
        OnPropertyChanged(nameof(SelectedPositionRealizedTotalIsPositive));
        OnPropertyChanged(nameof(SelectedPositionTradeAvgPrice));
        OnPropertyChanged(nameof(SelectedPositionTradeAvgPriceAsMoney));
        OnPropertyChanged(nameof(HasSelectedPositionRealized));
        OnPropertyChanged(nameof(SelectedPositionRoi1YDisplay));
        OnPropertyChanged(nameof(SelectedPositionRoi3YDisplay));
        OnPropertyChanged(nameof(SelectedPositionRoiCumDisplay));
        OnPropertyChanged(nameof(SelectedPositionXirr1YDisplay));
        OnPropertyChanged(nameof(SelectedPositionXirr3YDisplay));
        OnPropertyChanged(nameof(SelectedPositionXirrCumDisplay));
        OnPropertyChanged(nameof(SelectedPositionRoi1YIsPositive));
        OnPropertyChanged(nameof(SelectedPositionRoi3YIsPositive));
        OnPropertyChanged(nameof(SelectedPositionRoiCumIsPositive));
        OnPropertyChanged(nameof(SelectedPositionXirr1YIsPositive));
        OnPropertyChanged(nameof(SelectedPositionXirr3YIsPositive));
        OnPropertyChanged(nameof(SelectedPositionXirrCumIsPositive));
        RefreshSelectedPortfolioGroupDetail();

        // P4.8 — myvalue mode 重播 trade journal，trade reload 後要刷新 chart。
        _ = LoadAssetChartAsync();
    }

    [RelayCommand]
    private void CloseCashDetail() => SelectedCashRow = null;

    [RelayCommand]
    private void CloseLiabilityDetail() => SelectedLiabilityRow = null;

    [RelayCommand]
    private void ClosePositionDetail()
    {
        SelectedPortfolioGroupDetail = null;
        SelectedPositionRow = null;
    }

    private bool CanOpenSelectedPositionGroupDetail() =>
        SelectedPositionRow is { HasPortfolioGroupConflict: false };

    [RelayCommand(CanExecute = nameof(CanOpenSelectedPositionGroupDetail))]
    private void OpenSelectedPositionGroupDetail()
    {
        if (SelectedPositionRow is not { } row || row.HasPortfolioGroupConflict)
            return;

        SelectedPortfolioGroupDetail = BuildPortfolioGroupDetail(row.PortfolioGroupId ?? PortfolioGroup.DefaultId);
    }

    [RelayCommand]
    private void ClosePortfolioGroupDetail() => SelectedPortfolioGroupDetail = null;

    /// <summary>
    /// Permanently removes a position and all its associated Buy / StockDividend trade
    /// records (plus their fee children).
    /// </summary>
    [RelayCommand]
    private void RemovePosition(PortfolioRowViewModel row)
    {
        if (row is null)
            return;
        var msg = L("Portfolio.Detail.DeleteWarning", "刪除後無法復原，請確認不再需要後再操作。");
        AskConfirm(msg, async () =>
        {
            await _positionDeletionWorkflowService.DeleteAsync(
                new PositionDeletionRequest(row.AllEntryIds.ToList()));

            _positions.Remove(row);
            if (ReferenceEquals(SelectedPositionRow, row))
                SelectedPositionRow = null;
            RebuildTotals();

            await LoadTradesAsync();
            await ReloadAccountBalancesAsync();
        });
    }

    /// <summary>
    /// Permanently removes a liability (loan / credit card) and cascades all
    /// referencing trade rows (loan_label match for loans, liability_asset_id for credit cards).
    /// </summary>
    [RelayCommand]
    private void RemoveLiability(LiabilityRowViewModel row)
    {
        if (row is null)
            return;
        var msg = L("Portfolio.Detail.DeleteWarning", "刪除後無法復原，請確認不再需要後再操作。");
        AskConfirm(msg, async () =>
        {
            await _liabilityMutationWorkflowService.DeleteAsync(
                new LiabilityDeletionRequest(row.AssetId, row.Label));

            _liabilities.Remove(row);
            if (ReferenceEquals(SelectedLiabilityRow, row))
                SelectedLiabilityRow = null;
            RebuildTotals();

            await LoadTradesAsync();
            await ReloadAccountBalancesAsync();
        });
    }

    [RelayCommand]
    private void SwitchDetailTab(string tab) => DetailTab = tab;

    /// <summary>
    /// Opens the EditLiability dialog pre-populated from <paramref name="row"/>.
    /// Used by the Liability detail panel's Edit button.
    /// </summary>
    [RelayCommand]
    private void OpenEditLiability(LiabilityRowViewModel? row)
    {
        var target = row ?? SelectedLiabilityRow;
        if (target is null)
            return;
        EditLiabilityDialog.Open(target);
    }

    /// <summary>
    /// Wired from <c>EditLiabilityDialog.LiabilityUpdated</c>: rebuild the
    /// liability list so the row's snapshot picks up the new Name / rate /
    /// term, then re-select the matching row by AssetId so the side panel
    /// header stays in sync. Loan schedule is reloaded after re-selection.
    /// </summary>
    private async void OnLiabilityUpdated(object? sender, EventArgs e)
    {
        try
        {
            var editedAssetId = SelectedLiabilityRow?.AssetId;

            await LoadTradesAsync();
            await LoadLiabilitiesAsync();
            await ReloadAccountBalancesAsync();
            RebuildTotals();

            // Re-pick the same liability so the side panel keeps it open with
            // the refreshed VM (otherwise the header still shows the stale Name).
            if (editedAssetId is { } id)
            {
                var refreshed = Liabilities.FirstOrDefault(l => l.AssetId == id);
                if (refreshed is not null)
                {
                    SelectedLiabilityRow = refreshed;
                    if (refreshed.IsLoan)
                    {
                        refreshed.IsScheduleLoaded = false;
                        await Loan.LoadLoanScheduleAsync(refreshed);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _snackbar?.Error("更新後的重新載入失敗：" + ex.Message);
        }
    }
}
