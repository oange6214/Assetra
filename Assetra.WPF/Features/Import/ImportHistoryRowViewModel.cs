using Assetra.Core.Models.Import;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Import;

public sealed partial class ImportHistoryRowViewModel : ObservableObject
{
    public ImportBatchHistory Source { get; private set; }

    [ObservableProperty] private bool _isRolledBack;
    [ObservableProperty] private string? _rollbackHint;

    public ImportHistoryRowViewModel(ImportBatchHistory source)
    {
        Source = source;
        IsRolledBack = source.IsRolledBack;
    }

    public Guid Id => Source.Id;
    public string FileName => Source.FileName;
    public string FormatLabel => Source.Format.ToString();
    public string AppliedAtDisplay => Source.AppliedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
    public int RowsApplied => Source.RowsApplied;
    public int RowsSkipped => Source.RowsSkipped;
    public int RowsOverwritten => Source.RowsOverwritten;

    public string CountsDisplay =>
        $"+{RowsApplied} / ↻{RowsOverwritten} / ⊘{RowsSkipped}";

    public bool CanRollback => !IsRolledBack;

    public void MarkRolledBack()
    {
        Source = Source with { IsRolledBack = true, RolledBackAt = DateTimeOffset.UtcNow };
        IsRolledBack = true;
        OnPropertyChanged(nameof(CanRollback));
    }
}
