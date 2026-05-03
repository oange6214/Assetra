using System.Globalization;
using Assetra.Core.Models;
using Assetra.Core.Models.Reconciliation;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Reconciliation;

public partial class ReconciliationDiffRowViewModel : ObservableObject
{
    [ObservableProperty]
    private ReconciliationDiff _model;

    public ReconciliationDiffRowViewModel(ReconciliationDiff model, Trade? trade = null, decimal? tradeAmount = null)
    {
        _model = model;
        Trade = trade;
        TradeAmount = tradeAmount;
    }

    public Guid Id => Model.Id;
    public ReconciliationDiffKind Kind => Model.Kind;
    public ReconciliationDiffResolution Resolution => Model.Resolution;
    public Trade? Trade { get; }
    public decimal? TradeAmount { get; }

    public string KindDisplay => Kind switch
    {
        ReconciliationDiffKind.Missing => "Missing",
        ReconciliationDiffKind.Extra => "Extra",
        ReconciliationDiffKind.AmountMismatch => "AmountMismatch",
        _ => Kind.ToString(),
    };

    public string DateDisplay
    {
        get
        {
            var statementDate = Model.StatementRow?.Date.ToString("yyyy-MM-dd");
            var tradeDate = Trade?.TradeDate.ToString("yyyy-MM-dd");
            return CombineSides(statementDate, tradeDate);
        }
    }

    public string AmountDisplay
    {
        get
        {
            var statementAmount = Model.StatementRow is { } row
                ? row.Amount.ToString("N2", CultureInfo.InvariantCulture)
                : null;
            var tradeAmount = TradeAmount is { } amount
                ? amount.ToString("N2", CultureInfo.InvariantCulture)
                : null;
            return CombineSides(statementAmount, tradeAmount);
        }
    }

    public string CounterpartyDisplay => CombineSides(
        FirstNonEmpty(Model.StatementRow?.Counterparty, Model.StatementRow?.Memo),
        Trade is null ? null : DescribeTrade(Trade));

    public string TradeIdDisplay => Model.TradeId?.ToString("N")[..8] ?? string.Empty;

    public bool IsPending => Resolution == ReconciliationDiffResolution.Pending;
    public bool IsResolved => Resolution != ReconciliationDiffResolution.Pending;

    public bool IsMissing => Kind == ReconciliationDiffKind.Missing;
    public bool IsExtra => Kind == ReconciliationDiffKind.Extra;
    public bool IsAmountMismatch => Kind == ReconciliationDiffKind.AmountMismatch;

    public string ResolutionDisplay => Resolution.ToString();

    public void Refresh(ReconciliationDiff updated)
    {
        Model = updated;
        OnPropertyChanged(nameof(Resolution));
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(IsResolved));
        OnPropertyChanged(nameof(ResolutionDisplay));
    }

    private static string CombineSides(string? statementValue, string? tradeValue)
    {
        statementValue = Normalize(statementValue);
        tradeValue = Normalize(tradeValue);
        if (statementValue is null) return tradeValue ?? string.Empty;
        if (tradeValue is null || string.Equals(statementValue, tradeValue, StringComparison.Ordinal))
            return statementValue;
        return $"{statementValue} / {tradeValue}";
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string DescribeTrade(Trade trade)
    {
        var label = FirstNonEmpty(trade.Name, trade.Note, trade.Symbol, trade.Type.ToString())
            ?? trade.Type.ToString();
        return $"{label} ({trade.Type}, {trade.Id.ToString("N")[..8]})";
    }
}
