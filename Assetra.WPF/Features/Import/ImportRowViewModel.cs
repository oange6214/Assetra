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
    public double? OcrConfidence => Row.OcrConfidence;

    public string AmountDisplay => Amount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
    public string DateDisplay => Date.ToString("yyyy-MM-dd");

    /// <summary>OCR 來源且信心 &lt; 0.85 → UI 標紅供使用者重新確認。非 OCR 來源固定 false。</summary>
    public bool IsLowOcrConfidence => OcrConfidence is { } c && c < 0.85;

    public string OcrConfidenceDisplay => OcrConfidence is { } c
        ? c.ToString("P0", System.Globalization.CultureInfo.InvariantCulture)
        : string.Empty;

    /// <summary>For colored badge in preview grid.</summary>
    public string StatusTag => HasConflict
        ? Resolution switch
        {
            ImportConflictResolution.Skip => "warning",
            ImportConflictResolution.Overwrite => "danger",
            ImportConflictResolution.AddAnyway => "ontrack",
            _ => "warning",
        }
        : IsLowOcrConfidence ? "warning" : "ontrack";
}
