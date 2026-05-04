using System.Collections.ObjectModel;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Sync;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.PhysicalAsset;

public sealed record PhysicalAssetCategoryOption(PhysicalAssetCategory Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed partial class PhysicalAssetViewModel : ObservableObject
{
    private readonly IPhysicalAssetRepository _repository;
    private readonly IPhysicalAssetValuationService _valuation;
    private readonly ILocalizationService _localization;

    public ObservableCollection<PhysicalAssetRowViewModel> Assets { get; } = [];
    public ObservableCollection<PhysicalAssetCategoryOption> CategoryOptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoAssets))]
    private bool _isLoading;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private decimal _totalCurrentValue;
    [ObservableProperty] private decimal _totalUnrealizedGain;

    public bool HasAssets => Assets.Count > 0;
    public bool HasNoAssets => !IsLoading && Assets.Count == 0;

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
    [ObservableProperty] private bool _isFormOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    private Guid? _editingId;

    public bool IsEditing => EditingId.HasValue;

    public PhysicalAssetViewModel(
        IPhysicalAssetRepository repository,
        IPhysicalAssetValuationService valuation,
        ILocalizationService? localization = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(valuation);
        _repository = repository;
        _valuation = valuation;
        _localization = localization ?? NullLocalizationService.Instance;
        Assets.CollectionChanged += (_, _) => NotifyListStateChanged();
        _localization.LanguageChanged += OnLanguageChanged;
        RefreshLocalizedCategoryText();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var summaries = await _valuation.GetSummariesAsync().ConfigureAwait(true);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Assets.Clear();
                foreach (var s in summaries)
                    Assets.Add(new PhysicalAssetRowViewModel(
                        s,
                        GetCategoryDisplayName(s.Asset.Category)));
            });
            TotalCurrentValue = await _valuation.GetTotalCurrentValueAsync().ConfigureAwait(true);
            TotalUnrealizedGain = await _valuation.GetTotalUnrealizedGainAsync().ConfigureAwait(true);
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
    private void OpenAddForm()
    {
        ClearForm();
        IsFormOpen = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        FormError = null;
        if (string.IsNullOrWhiteSpace(FormName))    { FormError = "請輸入名稱"; return; }
        if (!ParseHelpers.TryParseDecimal(FormAcquisitionCost, out var cost))   { FormError = "購入成本格式錯誤"; return; }
        if (!ParseHelpers.TryParseDecimal(FormCurrentValue, out var current))   { FormError = "目前市值格式錯誤"; return; }

        var now = DateTimeOffset.UtcNow;
        var deviceId = string.Empty;
        var version = EditingId.HasValue
            ? (await _repository.GetByIdAsync(EditingId.Value).ConfigureAwait(true))?.Version.Bump(deviceId, now)
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
            await _repository.UpdateAsync(entity).ConfigureAwait(true);
        else
            await _repository.AddAsync(entity).ConfigureAwait(true);

        ClearForm();
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void Edit(PhysicalAssetRowViewModel row)
    {
        EditingId = row.Id;
        IsFormOpen = true;
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
        await _repository.RemoveAsync(row.Id).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
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
        IsFormOpen = false;
    }

    private void NotifyListStateChanged()
    {
        OnPropertyChanged(nameof(HasAssets));
        OnPropertyChanged(nameof(HasNoAssets));
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => RefreshLocalizedCategoryText();

    private void RefreshLocalizedCategoryText()
    {
        CategoryOptions.Clear();
        foreach (var category in Enum.GetValues<PhysicalAssetCategory>())
            CategoryOptions.Add(new PhysicalAssetCategoryOption(category, GetCategoryDisplayName(category)));

        foreach (var asset in Assets)
            asset.CategoryDisplay = GetCategoryDisplayName(asset.Category);
    }

    private string GetCategoryDisplayName(PhysicalAssetCategory category)
        => _localization.Get($"PhysicalAsset.Category.{category}", GetCategoryFallback(category));

    private static string GetCategoryFallback(PhysicalAssetCategory category)
        => category switch
        {
            PhysicalAssetCategory.Vehicle => "車輛",
            PhysicalAssetCategory.Jewelry => "珠寶",
            PhysicalAssetCategory.Art => "藝術品",
            PhysicalAssetCategory.Collectible => "收藏品",
            PhysicalAssetCategory.PreciousMetal => "貴金屬",
            PhysicalAssetCategory.Equipment => "設備",
            PhysicalAssetCategory.Other => "其他",
            _ => category.ToString(),
        };
}
