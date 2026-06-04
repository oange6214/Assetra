using System.Collections.ObjectModel;
using Assetra.Application.Calculators;
using Assetra.Core.Interfaces;
using Assetra.Core.Models.Calculators;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Calculators;

public sealed partial class LoanCalcViewModel : ObservableObject
{
    private readonly LoanAmortizationService _svc;
    private readonly ILocalizationService? _localization;

    [ObservableProperty] private string _principal = "3000000";
    [ObservableProperty] private string _annualRatePercent = "2.0";
    [ObservableProperty] private string _months = "360";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _monthlyPayment = "";
    [ObservableProperty] private string _totalInterest = "";
    [ObservableProperty] private string _totalPayment = "";

    private readonly ObservableCollection<LoanPaymentRow> _schedule = [];
    public ReadOnlyObservableCollection<LoanPaymentRow> Schedule { get; }

    public LoanCalcViewModel(LoanAmortizationService svc, ILocalizationService? localization = null)
    {
        _svc = svc;
        _localization = localization;
        Schedule = new ReadOnlyObservableCollection<LoanPaymentRow>(_schedule);
    }

    private string L(string key, string fallback) => _localization?.Get(key, fallback) ?? fallback;

    [RelayCommand]
    private void Calculate()
    {
        ErrorMessage = null;
        if (!ParseHelpers.TryParseDecimal(Principal, out var p) || p <= 0)
        { ErrorMessage = L("Calc.Loan.Error.Principal", "本金格式錯誤"); HasResult = false; return; }
        if (!ParseHelpers.TryParseDecimal(AnnualRatePercent, out var rate) || rate < 0)
        { ErrorMessage = L("Calc.Loan.Error.Rate", "利率格式錯誤"); HasResult = false; return; }
        if (!ParseHelpers.TryParseInt(Months, out var n) || n <= 0)
        { ErrorMessage = L("Calc.Loan.Error.Months", "期數格式錯誤"); HasResult = false; return; }

        var s = _svc.Calculate(new(p, rate / 100m, n));
        MonthlyPayment = s.MonthlyPayment.ToString("N0");
        TotalInterest = s.TotalInterest.ToString("N0");
        TotalPayment = s.TotalPayment.ToString("N0");
        _schedule.Clear();
        foreach (var row in s.Rows) _schedule.Add(row);
        HasResult = true;
    }
}
