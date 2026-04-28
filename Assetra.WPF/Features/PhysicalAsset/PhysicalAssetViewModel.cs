using System.Collections.ObjectModel;
using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.PhysicalAsset;

public sealed partial class PhysicalAssetViewModel : ObservableObject
{
    private readonly IPhysicalAssetRepository _repository;
    private readonly IPhysicalAssetValuationService _valuation;

    public ObservableCollection<PhysicalAssetRowViewModel> Assets { get; } = [];
    public IReadOnlyList<PhysicalAssetCategory> Categories { get; } =
        Enum.GetValues<PhysicalAssetCategory>();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private decimal _totalCurrentValue;
    [ObservableProperty] private decimal _totalUnrealizedGain;

    [ObservableProperty] private string _formName = string.Empty;
    [ObservableProperty] private PhysicalAssetCategory _formCategory = PhysicalAssetCategory.Vehicle;
    [ObservableProperty] private string _formDescription = string.Empty;
    [ObservableProperty] private string _formAcquisitionCost = "0";
    [ObservableProperty] private DateTime _formAcquisitionDate = DateTime.Today;
    [ObservableProperty] private string _formCurrentValue = "0";
    [ObservableProperty] private string _formValuationMethod = string.Empty;
    [ObservableProperty] private string _formCurrency = "TWD";
    [ObservableProperty] private string _formNotes = string.Empty;
    [ObservableProperty] private string? _formError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    private Guid? _editingId;

    public bool IsEditing => EditingId.HasValue;

    public PhysicalAssetViewModel(
        IPhysicalAssetRepository repository,
        IPhysicalAssetValuationService valuation)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(valuation);
        _repository = repository;
        _valuation = valuation;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var summaries = await _valuation.GetSummariesAsync().ConfigureAwait(false);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Assets.Clear();
                foreach (var s in summaries)
                    Assets.Add(new PhysicalAssetRowViewModel(s));
            });
            TotalCurrentValue = await _valuation.GetTotalCurrentValueAsync().ConfigureAwait(false);
            TotalUnrealizedGain = await _valuation.GetTotalUnrealizedGainAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        FormError = null;
        if (string.IsNullOrWhiteSpace(FormName))    { FormError = "請輸入名稱"; return; }
        if (!decimal.TryParse(FormAcquisitionCost, out var cost))   { FormError = "購入成本格式錯誤"; return; }
        if (!decimal.TryParse(FormCurrentValue, out var current))   { FormError = "目前市值格式錯誤"; return; }

        var now = DateTimeOffset.UtcNow;
        var deviceId = string.Empty;
        var version = EditingId.HasValue
            ? (await _repository.GetByIdAsync(EditingId.Value).ConfigureAwait(false))?.Version.Bump(deviceId, now)
              ?? EntityVersion.Initial(deviceId, now)
            : EntityVersion.Initial(deviceId, now);

        var entity = new Core.Models.MultiAsset.PhysicalAsset(
            Id: EditingId ?? Guid.NewGuid(),
            Name: FormName.Trim(),
            Category: FormCategory,
            Description: FormDescription.Trim(),
            AcquisitionCost: cost,
            AcquisitionDate: DateOnly.FromDateTime(FormAcquisitionDate),
            CurrentValue: current,
            ValuationMethod: FormValuationMethod.Trim(),
            Currency: FormCurrency.Trim(),
            Status: PhysicalAssetStatus.Active,
            Notes: string.IsNullOrWhiteSpace(FormNotes) ? null : FormNotes.Trim(),
            Version: version);

        if (EditingId.HasValue)
            await _repository.UpdateAsync(entity).ConfigureAwait(false);
        else
            await _repository.AddAsync(entity).ConfigureAwait(false);

        ClearForm();
        await LoadAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private void Edit(PhysicalAssetRowViewModel row)
    {
        EditingId = row.Id;
        FormName = row.Name;
        FormCategory = row.Category;
        FormDescription = row.Description;
        FormAcquisitionCost = row.AcquisitionCost.ToString();
        FormAcquisitionDate = row.AcquisitionDate.ToDateTime(TimeOnly.MinValue);
        FormCurrentValue = row.CurrentValue.ToString();
        FormValuationMethod = row.ValuationMethod;
        FormCurrency = row.Currency;
        FormNotes = row.Notes ?? string.Empty;
        FormError = null;
    }

    [RelayCommand]
    private async Task DeleteAsync(PhysicalAssetRowViewModel row)
    {
        await _repository.RemoveAsync(row.Id).ConfigureAwait(false);
        await LoadAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private void CancelEdit() => ClearForm();

    private void ClearForm()
    {
        EditingId = null;
        FormName = string.Empty;
        FormCategory = PhysicalAssetCategory.Vehicle;
        FormDescription = string.Empty;
        FormAcquisitionCost = "0";
        FormAcquisitionDate = DateTime.Today;
        FormCurrentValue = "0";
        FormValuationMethod = string.Empty;
        FormCurrency = "TWD";
        FormNotes = string.Empty;
        FormError = null;
    }
}
