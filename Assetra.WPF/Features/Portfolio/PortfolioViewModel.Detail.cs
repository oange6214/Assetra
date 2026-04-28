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
    [NotifyPropertyChangedFor(nameof(SelectedPositionRealizedTotal))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionTradeAvgPrice))]
    [NotifyPropertyChangedFor(nameof(HasSelectedPositionRealized))]
    private PortfolioRowViewModel? _selectedPositionRow;

    /// <summary>
    /// 是否有任何已實現損益資料（賣出價差或股息收入）。
    /// </summary>
    public bool HasSelectedPositionRealized =>
        SelectedPositionRealizedTotal != 0m || SelectedPositionDividendIncome > 0m;

    public bool HasSelectedCashRow => SelectedCashRow is not null;
    public bool HasSelectedLiabilityRow => SelectedLiabilityRow is not null;
    public bool HasSelectedPositionRow => SelectedPositionRow is not null;

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
    partial void OnSelectedPositionRowChanged(PortfolioRowViewModel? _) => DetailTab = "overview";

    // Cash account stats + filtered trades
    public IEnumerable<TradeRowViewModel> SelectedCashTrades =>
        SelectedCashRow is { } r
            ? Trades.Where(t => t.CashAccountId == r.Id)
                    .OrderByDescending(t => t.TradeDate)
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

    /// <summary>Sum of all CashDividend trades for the selected position (gross dividend income).</summary>
    public decimal SelectedPositionDividendIncome =>
        SelectedPositionRow is { } r
            ? Trades.Where(t => string.Equals(t.Symbol, r.Symbol, StringComparison.OrdinalIgnoreCase) &&
                                t.Type == TradeType.CashDividend)
                    .Sum(t => t.CashAmount ?? 0)
            : 0m;

    /// <summary>
    /// Realized capital gain — placeholder 0 so the realized P&L card layout renders correctly.
    /// </summary>
    public decimal SelectedPositionCapitalGain => 0m;

    public decimal SelectedPositionRealizedTotal =>
        SelectedPositionDividendIncome + SelectedPositionCapitalGain;

    [RelayCommand]
    private void CloseCashDetail() => SelectedCashRow = null;

    [RelayCommand]
    private void CloseLiabilityDetail() => SelectedLiabilityRow = null;

    [RelayCommand]
    private void ClosePositionDetail() => SelectedPositionRow = null;

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

            Positions.Remove(row);
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

            Liabilities.Remove(row);
            if (ReferenceEquals(SelectedLiabilityRow, row))
                SelectedLiabilityRow = null;
            RebuildTotals();

            await LoadTradesAsync();
            await ReloadAccountBalancesAsync();
        });
    }

    [RelayCommand]
    private void SwitchDetailTab(string tab) => DetailTab = tab;
}
