using System.Globalization;
using Assetra.Core.Models.Reconciliation;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Reconciliation;

public partial class ReconciliationDiffRowViewModel : ObservableObject
{
    [ObservableProperty]
    private ReconciliationDiff _model;

    public ReconciliationDiffRowViewModel(ReconciliationDiff model)
    {
        _model = model;
    }

    public Guid Id => Model.Id;
    public ReconciliationDiffKind Kind => Model.Kind;
    public ReconciliationDiffResolution Resolution => Model.Resolution;

    public string KindDisplay => Kind switch
    {
        ReconciliationDiffKind.Missing => "Missing",
        ReconciliationDiffKind.Extra => "Extra",
        ReconciliationDiffKind.AmountMismatch => "AmountMismatch",
        _ => Kind.ToString(),
    };

    public string DateDisplay => Model.StatementRow?.Date.ToString("yyyy-MM-dd")
        ?? string.Empty;

    public string AmountDisplay => Model.StatementRow is { } row
        ? row.Amount.ToString("N2", CultureInfo.InvariantCulture)
        : string.Empty;

    public string CounterpartyDisplay => Model.StatementRow?.Counterparty
        ?? string.Empty;

    public string TradeIdDisplay => Model.TradeId?.ToString("N")[..8] ?? string.Empty;

    public bool IsPending => Resolution == ReconciliationDiffResolution.Pending;
    public bool IsResolved => Resolution != ReconciliationDiffResolution.Pending;

    public string ResolutionDisplay => Resolution.ToString();

    public void Refresh(ReconciliationDiff updated)
    {
        Model = updated;
        OnPropertyChanged(nameof(Resolution));
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(IsResolved));
        OnPropertyChanged(nameof(ResolutionDisplay));
    }
}
