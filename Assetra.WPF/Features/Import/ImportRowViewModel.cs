using Assetra.Core.Models.Import;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Import;

public sealed partial class ImportRowViewModel : ObservableObject
{
    public ImportPreviewRow Row { get; }
    public bool HasConflict { get; }
    public Guid? ExistingTradeId { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusTag))]
    private ImportConflictResolution _resolution;

    public ImportRowViewModel(ImportPreviewRow row)
    {
        Row = row;
        HasConflict = false;
        Resolution = ImportConflictResolution.AddAnyway;
    }

    public ImportRowViewModel(ImportPreviewRow row, ImportConflict conflict)
    {
        Row = row;
        HasConflict = true;
        ExistingTradeId = conflict.ExistingTradeId;
        Resolution = conflict.Resolution;
    }

    public int RowIndex => Row.RowIndex;
    public DateOnly Date => Row.Date;
    public decimal Amount => Row.Amount;
    public string? Counterparty => Row.Counterparty;
    public string? Memo => Row.Memo;
    public string? Symbol => Row.Symbol;
    public decimal? Quantity => Row.Quantity;

    public string AmountDisplay => Amount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
    public string DateDisplay => Date.ToString("yyyy-MM-dd");

    /// <summary>For colored badge in preview grid.</summary>
    public string StatusTag => HasConflict
        ? Resolution switch
        {
            ImportConflictResolution.Skip => "warning",
            ImportConflictResolution.Overwrite => "danger",
            ImportConflictResolution.AddAnyway => "ontrack",
            _ => "warning",
        }
        : "ontrack";
}
