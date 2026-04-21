using Assetra.Core.Models;

namespace Assetra.WPF.Features.Portfolio;

internal sealed class PortfolioTradeDialogController
{
    public TradeDialogCreateState CreateOpenState(CashAccountRowViewModel? defaultCashAccount) =>
        new(
            TxDate: DateTime.Today,
            TxCashAccount: defaultCashAccount,
            TxUseCashAccount: true,
            TxBuyAssetType: "stock",
            TxBuyPriceMode: "unit",
            TxDivInputMode: "perShare",
            TxCommissionDiscount: "1.0",
            TxLoanStartDate: DateTime.Today,
            AddBuyDate: DateTime.Today);

    public TradeDialogEditState CreateEditState(
        TradeRowViewModel row,
        IReadOnlyList<PortfolioRowViewModel> positions,
        IReadOnlyList<CashAccountRowViewModel> cashAccounts)
    {
        var txType = MapType(row.Type);
        var cashAccount = row.CashAccountId is { } cashAcc
            ? cashAccounts.FirstOrDefault(c => c.Id == cashAcc)
            : null;

        return row.Type switch
        {
            TradeType.Buy => new TradeDialogEditState(
                row.Id, row.TradeDate.ToLocalTime(), txType, row.Note ?? string.Empty,
                TxCashAccount: cashAccount,
                TxUseCashAccount: cashAccount is not null,
                AddSymbol: row.Symbol,
                AddPrice: row.Price.ToString("F4"),
                AddQuantity: row.Quantity.ToString(),
                AddBuyDate: row.TradeDate.ToLocalTime()),

            TradeType.Sell => new TradeDialogEditState(
                row.Id, row.TradeDate.ToLocalTime(), txType, row.Note ?? string.Empty,
                TxAmount: row.Price.ToString("F4"),
                TxCashAccount: cashAccount,
                TxUseCashAccount: cashAccount is not null,
                TxSellPosition: positions.FirstOrDefault(p => p.Symbol == row.Symbol),
                TxSellQuantity: row.Quantity.ToString(),
                SellCashAccount: cashAccount,
                SellPriceInput: row.Price.ToString("F4")),

            TradeType.CashDividend => new TradeDialogEditState(
                row.Id, row.TradeDate.ToLocalTime(), txType, row.Note ?? string.Empty,
                TxCashAccount: cashAccount,
                TxUseCashAccount: cashAccount is not null,
                TxDivPosition: positions.FirstOrDefault(p => p.Symbol == row.Symbol),
                TxDivPerShare: row.Price > 0 ? row.Price.ToString("F4") : string.Empty),

            TradeType.StockDividend => new TradeDialogEditState(
                row.Id, row.TradeDate.ToLocalTime(), txType, row.Note ?? string.Empty,
                TxStockDivPosition: positions.FirstOrDefault(p => p.Symbol == row.Symbol),
                TxStockDivNewShares: row.Quantity.ToString()),

            TradeType.Income or TradeType.Deposit or TradeType.Withdrawal => new TradeDialogEditState(
                row.Id, row.TradeDate.ToLocalTime(), txType, row.Note ?? string.Empty,
                TxAmount: row.CashAmount?.ToString("F0") ?? string.Empty,
                TxCashAccount: cashAccount),

            TradeType.LoanBorrow => new TradeDialogEditState(
                row.Id, row.TradeDate.ToLocalTime(), txType, row.Note ?? string.Empty,
                TxAmount: row.CashAmount?.ToString("F0") ?? string.Empty,
                TxLoanLabel: row.LoanLabel ?? row.Name,
                TxCashAccount: cashAccount,
                TxUseCashAccount: cashAccount is not null),

            TradeType.LoanRepay => new TradeDialogEditState(
                row.Id, row.TradeDate.ToLocalTime(), txType, row.Note ?? string.Empty,
                TxLoanLabel: row.LoanLabel ?? row.Name,
                TxCashAccount: cashAccount,
                TxUseCashAccount: cashAccount is not null,
                TxPrincipal: row.Principal.HasValue
                    ? Math.Round(row.Principal.Value, 0).ToString("F0")
                    : row.CashAmount?.ToString("F0") ?? string.Empty,
                TxInterestPaid: row.InterestPaid.HasValue
                    ? Math.Round(row.InterestPaid.Value, 0).ToString("F0")
                    : "0"),

            _ => new TradeDialogEditState(
                row.Id,
                row.TradeDate.ToLocalTime(),
                txType,
                row.Note ?? string.Empty)
        };
    }

    private static string MapType(TradeType type) => type switch
    {
        TradeType.Buy => "buy",
        TradeType.Sell => "sell",
        TradeType.Income => "income",
        TradeType.CashDividend => "cashDiv",
        TradeType.StockDividend => "stockDiv",
        TradeType.Deposit => "deposit",
        TradeType.Withdrawal => "withdrawal",
        TradeType.Transfer => "transfer",
        TradeType.LoanBorrow => "loanBorrow",
        TradeType.LoanRepay => "loanRepay",
        _ => "income",
    };
}

internal sealed record TradeDialogCreateState(
    DateTime TxDate,
    CashAccountRowViewModel? TxCashAccount,
    bool TxUseCashAccount,
    string TxBuyAssetType,
    string TxBuyPriceMode,
    string TxDivInputMode,
    string TxCommissionDiscount,
    DateTime TxLoanStartDate,
    DateTime AddBuyDate);

internal sealed record TradeDialogEditState(
    Guid EditingTradeId,
    DateTime TxDate,
    string TxType,
    string TxNote,
    string TxAmount = "",
    CashAccountRowViewModel? TxCashAccount = null,
    bool TxUseCashAccount = true,
    PortfolioRowViewModel? TxDivPosition = null,
    string TxDivPerShare = "",
    PortfolioRowViewModel? TxStockDivPosition = null,
    string TxStockDivNewShares = "",
    string TxLoanLabel = "",
    string TxPrincipal = "",
    string TxInterestPaid = "",
    PortfolioRowViewModel? TxSellPosition = null,
    string TxSellQuantity = "",
    CashAccountRowViewModel? SellCashAccount = null,
    string SellPriceInput = "",
    string AddSymbol = "",
    string AddPrice = "",
    string AddQuantity = "",
    DateTime? AddBuyDate = null);
