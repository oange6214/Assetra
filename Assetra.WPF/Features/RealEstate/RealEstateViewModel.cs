using System.Collections.ObjectModel;
using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models.MultiAsset;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.RealEstate;

public sealed partial class RealEstateViewModel : ObservableObject
{
    private readonly IRealEstateRepository _repository;
    private readonly IRealEstateValuationService _valuation;

    public ObservableCollection<RealEstateRowViewModel> Properties { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private decimal _totalEquity;

    // ── Add/Edit form ──
    [ObservableProperty] private string _formName = string.Empty;
    [ObservableProperty] private string _formAddress = string.Empty;
    [ObservableProperty] private string _formPurchasePrice = string.Empty;
    [ObservableProperty] private DateTime _formPurchaseDate = DateTime.Today;
    [ObservableProperty] private string _formCurrentValue = string.Empty;
    [ObservableProperty] private string _formMortgageBalance = "0";
    [ObservableProperty] private string _formCurrency = "TWD";
    [ObservableProperty] private bool _formIsRental;
    [ObservableProperty] private string _formNotes = string.Empty;
    [ObservableProperty] private string? _formError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    private Guid? _editingId;

    public bool IsEditing => EditingId.HasValue;

    public RealEstateViewModel(
        IRealEstateRepository repository,
        IRealEstateValuationService valuation)
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
            var summaries = await _valuation.GetValuationSummariesAsync().ConfigureAwait(false);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Properties.Clear();
                foreach (var s in summaries)
                    Properties.Add(new RealEstateRowViewModel(s));
            });
            TotalEquity = await _valuation.GetTotalEquityAsync().ConfigureAwait(false);
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
        if (!decimal.TryParse(FormPurchasePrice, out var purchasePrice)) { FormError = "購入金額格式錯誤"; return; }
        if (!decimal.TryParse(FormCurrentValue, out var currentValue))  { FormError = "目前市值格式錯誤"; return; }
        if (!decimal.TryParse(FormMortgageBalance, out var mortgage))   { FormError = "房貸餘額格式錯誤"; return; }

        var entity = new Core.Models.MultiAsset.RealEstate(
            Id: EditingId ?? Guid.NewGuid(),
            Name: FormName.Trim(),
            Address: FormAddress.Trim(),
            PurchasePrice: purchasePrice,
            PurchaseDate: DateOnly.FromDateTime(FormPurchaseDate),
            CurrentValue: currentValue,
            MortgageBalance: mortgage,
            Currency: FormCurrency.Trim(),
            IsRental: FormIsRental,
            Status: RealEstateStatus.Active,
            Notes: string.IsNullOrWhiteSpace(FormNotes) ? null : FormNotes.Trim(),
            Version: new());

        if (EditingId.HasValue)
            await _repository.UpdateAsync(entity).ConfigureAwait(false);
        else
            await _repository.AddAsync(entity).ConfigureAwait(false);

        ClearForm();
        await LoadAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private void Edit(RealEstateRowViewModel row)
    {
        EditingId = row.Id;
        FormName = row.Name;
        FormAddress = row.Address;
        FormPurchasePrice = row.PurchasePrice.ToString();
        FormPurchaseDate = row.PurchaseDate.ToDateTime(TimeOnly.MinValue);
        FormCurrentValue = row.CurrentValue.ToString();
        FormMortgageBalance = row.MortgageBalance.ToString();
        FormCurrency = row.Currency;
        FormIsRental = row.IsRental;
        FormNotes = row.Notes ?? string.Empty;
        FormError = null;
    }

    [RelayCommand]
    private async Task DeleteAsync(RealEstateRowViewModel row)
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
        FormAddress = string.Empty;
        FormPurchasePrice = string.Empty;
        FormPurchaseDate = DateTime.Today;
        FormCurrentValue = string.Empty;
        FormMortgageBalance = "0";
        FormCurrency = "TWD";
        FormIsRental = false;
        FormNotes = string.Empty;
        FormError = null;
    }
}
