using System.Collections.ObjectModel;
using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Insurance;

public sealed partial class InsurancePolicyViewModel : ObservableObject
{
    private readonly IInsurancePolicyRepository _repository;
    private readonly IInsuranceCashValueCalculator _calculator;

    public ObservableCollection<InsurancePolicyRowViewModel> Policies { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private decimal _totalCashValue;
    [ObservableProperty] private decimal _totalAnnualPremium;

    // ── Add/Edit form ──
    [ObservableProperty] private string _formName = string.Empty;
    [ObservableProperty] private string _formPolicyNumber = string.Empty;
    [ObservableProperty] private InsuranceType _formType = InsuranceType.WholeLife;
    [ObservableProperty] private string _formInsurer = string.Empty;
    [ObservableProperty] private DateTime _formStartDate = DateTime.Today;
    [ObservableProperty] private DateTime? _formMaturityDate;
    [ObservableProperty] private string _formFaceValue = "0";
    [ObservableProperty] private string _formCurrentCashValue = "0";
    [ObservableProperty] private string _formAnnualPremium = string.Empty;
    [ObservableProperty] private string _formCurrency = "TWD";
    [ObservableProperty] private string _formNotes = string.Empty;
    [ObservableProperty] private string? _formError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    private Guid? _editingId;

    public bool IsEditing => EditingId.HasValue;

    public IReadOnlyList<InsuranceType> InsuranceTypes { get; } =
        Enum.GetValues<InsuranceType>().ToList().AsReadOnly();

    public InsurancePolicyViewModel(
        IInsurancePolicyRepository repository,
        IInsuranceCashValueCalculator calculator)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(calculator);
        _repository = repository;
        _calculator = calculator;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var summaries = await _calculator.GetCashValueSummariesAsync().ConfigureAwait(false);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Policies.Clear();
                foreach (var s in summaries)
                    Policies.Add(new InsurancePolicyRowViewModel(s));
            });
            TotalCashValue = await _calculator.GetTotalCashValueAsync().ConfigureAwait(false);
            TotalAnnualPremium = await _calculator.GetTotalAnnualPremiumAsync().ConfigureAwait(false);
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
        if (string.IsNullOrWhiteSpace(FormName))        { FormError = "請輸入保單名稱"; return; }
        if (!decimal.TryParse(FormAnnualPremium, out var premium))   { FormError = "年繳保費格式錯誤"; return; }
        if (!decimal.TryParse(FormFaceValue, out var faceValue))     { FormError = "保額格式錯誤"; return; }
        if (!decimal.TryParse(FormCurrentCashValue, out var cashVal)) { FormError = "現金價值格式錯誤"; return; }

        var now = DateTimeOffset.UtcNow;
        var deviceId = string.Empty;
        var version = EditingId.HasValue
            ? (await _repository.GetByIdAsync(EditingId.Value).ConfigureAwait(false))?.Version.Bump(deviceId, now)
              ?? EntityVersion.Initial(deviceId, now)
            : EntityVersion.Initial(deviceId, now);

        var policy = new InsurancePolicy(
            Id: EditingId ?? Guid.NewGuid(),
            Name: FormName.Trim(),
            PolicyNumber: FormPolicyNumber.Trim(),
            Type: FormType,
            Insurer: FormInsurer.Trim(),
            StartDate: DateOnly.FromDateTime(FormStartDate),
            MaturityDate: FormMaturityDate.HasValue ? DateOnly.FromDateTime(FormMaturityDate.Value) : null,
            FaceValue: faceValue,
            CurrentCashValue: cashVal,
            AnnualPremium: premium,
            Currency: FormCurrency.Trim(),
            Status: InsurancePolicyStatus.Active,
            Notes: string.IsNullOrWhiteSpace(FormNotes) ? null : FormNotes.Trim(),
            Version: version);

        if (EditingId.HasValue)
            await _repository.UpdateAsync(policy).ConfigureAwait(false);
        else
            await _repository.AddAsync(policy).ConfigureAwait(false);

        ClearForm();
        await LoadAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private void Edit(InsurancePolicyRowViewModel row)
    {
        EditingId = row.Id;
        FormName = row.Name;
        FormPolicyNumber = row.PolicyNumber;
        FormType = row.Type;
        FormInsurer = row.Insurer;
        FormStartDate = row.StartDate.ToDateTime(TimeOnly.MinValue);
        FormMaturityDate = row.MaturityDate?.ToDateTime(TimeOnly.MinValue);
        FormFaceValue = row.FaceValue.ToString();
        FormCurrentCashValue = row.CashValue.ToString();
        FormAnnualPremium = row.AnnualPremium.ToString();
        FormCurrency = row.Currency;
        FormNotes = row.Notes ?? string.Empty;
        FormError = null;
    }

    [RelayCommand]
    private async Task DeleteAsync(InsurancePolicyRowViewModel row)
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
        FormPolicyNumber = string.Empty;
        FormType = InsuranceType.WholeLife;
        FormInsurer = string.Empty;
        FormStartDate = DateTime.Today;
        FormMaturityDate = null;
        FormFaceValue = "0";
        FormCurrentCashValue = "0";
        FormAnnualPremium = string.Empty;
        FormCurrency = "TWD";
        FormNotes = string.Empty;
        FormError = null;
    }
}
