using Assetra.Application.Calculators;
using Assetra.Core.Interfaces;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Calculators;

public sealed partial class RuleOf72CalcViewModel : ObservableObject
{
    private readonly RuleOf72Calculator _svc;
    private readonly ILocalizationService? _localization;

    [ObservableProperty] private string _annualRatePercent = "6.0";
    [ObservableProperty] private string _targetYears = "10";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _doublingYears = "";
    [ObservableProperty] private string _requiredRate = "";

    // Lookup table: 2×/4×/8× doublings at current rate
    [ObservableProperty] private string _double2x = "";
    [ObservableProperty] private string _double4x = "";
    [ObservableProperty] private string _double8x = "";

    public RuleOf72CalcViewModel(RuleOf72Calculator svc, ILocalizationService? localization = null)
    {
        _svc = svc;
        _localization = localization;
    }

    private string L(string key, string fallback) => _localization?.Get(key, fallback) ?? fallback;

    [RelayCommand]
    private void Calculate()
    {
        ErrorMessage = null;

        var rateEntered = !string.IsNullOrWhiteSpace(AnnualRatePercent);
        var rateOk = rateEntered && ParseHelpers.TryParseDecimal(AnnualRatePercent, out var rate) && rate > 0;
        if (rateEntered && !rateOk)
        { ErrorMessage = L("Calc.Rule72.Error.RateInvalid", "年報酬率格式錯誤，請輸入正數"); HasResult = false; return; }
        ParseHelpers.TryParseDecimal(AnnualRatePercent, out rate);

        var yearsEntered = !string.IsNullOrWhiteSpace(TargetYears);
        var yearsOk = yearsEntered && ParseHelpers.TryParseDecimal(TargetYears, out var yrs) && yrs > 0;
        if (yearsEntered && !yearsOk)
        { ErrorMessage = L("Calc.Rule72.Error.YearsInvalid", "年數格式錯誤，請輸入正數"); HasResult = false; return; }
        ParseHelpers.TryParseDecimal(TargetYears, out yrs);

        if (!rateOk && !yearsOk)
        { ErrorMessage = L("Calc.Rule72.Error.BothInvalid", "請輸入有效的年報酬率或年數"); HasResult = false; return; }

        HasResult = true;

        if (rateOk)
        {
            var r = (double)rate;
            DoublingYears = _svc.DoublingYears(r).ToString("F1");
            Double2x = _svc.DoublingYears(r).ToString("F1");
            Double4x = (_svc.DoublingYears(r) * 2).ToString("F1");
            Double8x = (_svc.DoublingYears(r) * 3).ToString("F1");
        }

        if (yearsOk)
        {
            RequiredRate = _svc.RequiredRatePercent((double)yrs).ToString("F2");
        }
    }
}
