using System.Collections.ObjectModel;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Sync;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Insurance;

public sealed record InsuranceTypeOption(InsuranceType Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed partial class InsurancePolicyViewModel : ObservableObject
{
    private readonly IInsurancePolicyRepository _repository;
    private readonly IInsuranceCashValueCalculator _calculator;
    private readonly ILocalizationService _localization;

    public ObservableCollection<InsurancePolicyRowViewModel> Policies { get; } = [];
    public ObservableCollection<InsuranceTypeOption> InsuranceTypeOptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoPolicies))]
    private bool _isLoading;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private decimal _totalCashValue;
    [ObservableProperty] private decimal _totalAnnualPremium;

    public bool HasPolicies => Policies.Count > 0;
    public bool HasNoPolicies => !IsLoading && Policies.Count == 0;

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
    [ObservableProperty] private bool _isFormOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    private Guid? _editingId;

    public bool IsEditing => EditingId.HasValue;

    public InsurancePolicyViewModel(
        IInsurancePolicyRepository repository,
        IInsuranceCashValueCalculator calculator,
        ILocalizationService? localization = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(calculator);
        _repository = repository;
        _calculator = calculator;
        _localization = localization ?? NullLocalizationService.Instance;
        Policies.CollectionChanged += (_, _) => NotifyListStateChanged();
        _localization.LanguageChanged += OnLanguageChanged;
        RefreshLocalizedInsuranceTypeText();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var summaries = await _calculator.GetCashValueSummariesAsync().ConfigureAwait(true);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Policies.Clear();
                foreach (var s in summaries)
                    Policies.Add(new InsurancePolicyRowViewModel(
                        s,
                        GetInsuranceTypeDisplayName(s.Policy.Type)));
            });
            TotalCashValue = await _calculator.GetTotalCashValueAsync().ConfigureAwait(true);
            TotalAnnualPremium = await _calculator.GetTotalAnnualPremiumAsync().ConfigureAwait(true);
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
        if (string.IsNullOrWhiteSpace(FormName))        { FormError = "請輸入保單名稱"; return; }
        if (!ParseHelpers.TryParseDecimal(FormAnnualPremium, out var premium))   { FormError = "年繳保費格式錯誤"; return; }
        if (!ParseHelpers.TryParseDecimal(FormFaceValue, out var faceValue))     { FormError = "保額格式錯誤"; return; }
        if (!ParseHelpers.TryParseDecimal(FormCurrentCashValue, out var cashVal)) { FormError = "現金價值格式錯誤"; return; }

        var now = DateTimeOffset.UtcNow;
        var deviceId = string.Empty;
        var version = EditingId.HasValue
            ? (await _repository.GetByIdAsync(EditingId.Value).ConfigureAwait(true))?.Version.Bump(deviceId, now)
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
            await _repository.UpdateAsync(policy).ConfigureAwait(true);
        else
            await _repository.AddAsync(policy).ConfigureAwait(true);

        ClearForm();
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void Edit(InsurancePolicyRowViewModel row)
    {
        EditingId = row.Id;
        IsFormOpen = true;
        FormName = row.Name;
        FormPolicyNumber = row.PolicyNumber;
        FormType = row.Type;
        FormInsurer = row.Insurer;
        FormStartDate = row.StartDate.ToDateTime(TimeOnly.MinValue);
        FormMaturityDate = row.MaturityDate?.ToDateTime(TimeOnly.MinValue);
        FormFaceValue = row.FaceValue.ToString();
        FormCurrentCashValue = row.OriginalCashValue.ToString();
        FormAnnualPremium = row.OriginalAnnualPremium.ToString();
        FormCurrency = row.OriginalCurrency;
        FormNotes = row.Notes ?? string.Empty;
        FormError = null;
    }

    [RelayCommand]
    private async Task DeleteAsync(InsurancePolicyRowViewModel row)
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
        IsFormOpen = false;
    }

    private void NotifyListStateChanged()
    {
        OnPropertyChanged(nameof(HasPolicies));
        OnPropertyChanged(nameof(HasNoPolicies));
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => RefreshLocalizedInsuranceTypeText();

    private void RefreshLocalizedInsuranceTypeText()
    {
        InsuranceTypeOptions.Clear();
        foreach (var type in Enum.GetValues<InsuranceType>())
            InsuranceTypeOptions.Add(new InsuranceTypeOption(type, GetInsuranceTypeDisplayName(type)));

        foreach (var policy in Policies)
            policy.TypeDisplay = GetInsuranceTypeDisplayName(policy.Type);
    }

    private string GetInsuranceTypeDisplayName(InsuranceType type)
        => _localization.Get($"Insurance.Type.{type}", GetInsuranceTypeFallback(type));

    private static string GetInsuranceTypeFallback(InsuranceType type)
        => type switch
        {
            InsuranceType.WholeLife => "終身壽險",
            InsuranceType.TermLife => "定期壽險",
            InsuranceType.Endowment => "儲蓄險",
            InsuranceType.Annuity => "年金險",
            InsuranceType.Universal => "萬能壽險",
            InsuranceType.Other => "其他",
            _ => type.ToString(),
        };
}
