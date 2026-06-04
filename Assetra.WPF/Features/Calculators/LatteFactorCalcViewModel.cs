using Assetra.Application.Calculators;
using Assetra.Core.Interfaces;
using Assetra.Core.Models.Calculators;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Calculators;

public sealed partial class LatteFactorCalcViewModel : ObservableObject
{
    private readonly LatteFactorCalculator _svc;
    private readonly ILocalizationService? _localization;

    [ObservableProperty] private string _amountPerSpend = "150";
    [ObservableProperty] private string _annualReturnPercent = "6.0";
    [ObservableProperty] private string _years = "20";
    [ObservableProperty] private LatteFrequency _selectedFrequency = LatteFrequency.Daily;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _totalContributed = "";
    [ObservableProperty] private string _futureValue = "";
    [ObservableProperty] private string _gain = "";

    public LatteFrequency[] Frequencies { get; } = (LatteFrequency[])Enum.GetValues(typeof(LatteFrequency));

    public LatteFactorCalcViewModel(LatteFactorCalculator svc, ILocalizationService? localization = null)
    {
        _svc = svc;
        _localization = localization;
    }

    private string L(string key, string fallback) => _localization?.Get(key, fallback) ?? fallback;

    [RelayCommand]
    private void Calculate()
    {
        ErrorMessage = null;
        if (!ParseHelpers.TryParseDecimal(AmountPerSpend, out var amount) || amount < 0)
        { ErrorMessage = L("Calc.Latte.Error.Amount", "金額格式錯誤"); HasResult = false; return; }
        if (!ParseHelpers.TryParseDecimal(AnnualReturnPercent, out var rate) || rate < 0)
        { ErrorMessage = L("Calc.Latte.Error.Rate", "報酬率格式錯誤"); HasResult = false; return; }
        if (!ParseHelpers.TryParseInt(Years, out var years) || years <= 0)
        { ErrorMessage = L("Calc.Latte.Error.Years", "年數格式錯誤"); HasResult = false; return; }

        var r = _svc.Calculate(new(amount, SelectedFrequency, rate / 100m, years));
        TotalContributed = r.TotalContributed.ToString("N0");
        FutureValue = r.FutureValue.ToString("N0");
        Gain = r.Gain.ToString("N0");
        HasResult = true;
    }
}
