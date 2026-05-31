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

    private readonly ObservableCollection<InsurancePolicyRowViewModel> _policies = [];
    private readonly ObservableCollection<InsuranceTypeOption> _insuranceTypeOptions = [];

    public ReadOnlyObservableCollection<InsurancePolicyRowViewModel> Policies { get; }
    public ReadOnlyObservableCollection<InsuranceTypeOption> InsuranceTypeOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoPolicies))]
    private bool _isLoading;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private decimal _totalCashValue;
    [ObservableProperty] private decimal _totalAnnualPremium;

    public bool HasPolicies => Policies.Count > 0;
    public bool HasNoPolicies => !IsLoading && Policies.Count == 0;
    public int PolicyCount => Policies.Count;

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
    [ObservableProperty] private bool _isDeleteConfirmOpen;
    [ObservableProperty] private string _deleteTargetName = string.Empty;
    private InsurancePolicyRowViewModel? _pendingDelete;

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
        Policies = new ReadOnlyObservableCollection<InsurancePolicyRowViewModel>(_policies);
        InsuranceTypeOptions = new ReadOnlyObservableCollection<InsuranceTypeOption>(_insuranceTypeOptions);
        _policies.CollectionChanged += (_, _) => NotifyListStateChanged();
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
                _policies.Clear();
                foreach (var s in summaries)
                    _policies.Add(new InsurancePolicyRowViewModel(
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
        if (string.IsNullOrWhiteSpace(FormName))
        { FormError = "請輸入保單名稱"; return; }
        if (!ParseHelpers.TryParseDecimal(FormAnnualPremium, out var premium))
        { FormError = "年繳保費格式錯誤"; return; }
        if (!ParseHelpers.TryParseDecimal(FormFaceValue, out var faceValue))
        { FormError = "保額格式錯誤"; return; }
        if (!ParseHelpers.TryParseDecimal(FormCurrentCashValue, out var cashVal))
        { FormError = "現金價值格式錯誤"; return; }

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
    private void Delete(InsurancePolicyRowViewModel row)
    {
        _pendingDelete = row;
        DeleteTargetName = row.Name;
        IsDeleteConfirmOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        var row = _pendingDelete;
        if (row is null)
        {
            CancelDelete();
            return;
        }

        await _repository.RemoveAsync(row.Id).ConfigureAwait(true);
        if (EditingId == row.Id)
            ClearForm();

        CancelDelete();
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void CancelDelete()
    {
        _pendingDelete = null;
        DeleteTargetName = string.Empty;
        IsDeleteConfirmOpen = false;
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
        CancelDelete();
    }

    private void NotifyListStateChanged()
    {
        OnPropertyChanged(nameof(HasPolicies));
        OnPropertyChanged(nameof(HasNoPolicies));
        OnPropertyChanged(nameof(PolicyCount));
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => RefreshLocalizedInsuranceTypeText();

    private void RefreshLocalizedInsuranceTypeText()
    {
        _insuranceTypeOptions.Clear();
        foreach (var type in Enum.GetValues<InsuranceType>())
            _insuranceTypeOptions.Add(new InsuranceTypeOption(type, GetInsuranceTypeDisplayName(type)));

        foreach (var policy in _policies)
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
