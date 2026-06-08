using Assetra.Application.Calculators;
using Assetra.Core.Interfaces;
using Assetra.Core.Models.Calculators;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Calculators;

public sealed partial class RentVsBuyCalcViewModel : ObservableObject
{
    private readonly RentVsBuyCalculator _svc;
    private readonly ILocalizationService? _localization;

    // Buy inputs
    [ObservableProperty] private string _homePrice = "10000000";
    [ObservableProperty] private string _downPayment = "2000000";
    [ObservableProperty] private string _mortgageRatePercent = "2.0";
    [ObservableProperty] private string _loanYears = "30";
    [ObservableProperty] private string _holdingCostRatePercent = "1.0";
    [ObservableProperty] private string _appreciationRatePercent = "2.0";
    // Rent inputs
    [ObservableProperty] private string _monthlyRent = "25000";
    [ObservableProperty] private string _rentIncreasePercent = "2.0";
    [ObservableProperty] private string _investmentReturnPercent = "4.0";
    // Shared
    [ObservableProperty] private string _compareYears = "10";
    [ObservableProperty] private string _purchaseCostRatePercent = "2.0";
    [ObservableProperty] private string _sellCostRatePercent = "2.0";

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _buyerEndingNetWorth = "";
    [ObservableProperty] private string _renterEndingNetWorth = "";
    [ObservableProperty] private string _netWorthDifference = "";
    [ObservableProperty] private string _winnerLabel = "";
    [ObservableProperty] private string _breakEvenYear = "";
    [ObservableProperty] private string _resultExplanation = "";
    [ObservableProperty] private string _breakEvenExplanation = "";

    public RentVsBuyCalcViewModel(RentVsBuyCalculator svc, ILocalizationService? localization = null)
    {
        _svc = svc;
        _localization = localization;
    }

    private string L(string key, string fallback) => _localization?.Get(key, fallback) ?? fallback;

    [RelayCommand]
    private void Calculate()
    {
        ErrorMessage = null;
        if (!ParseHelpers.TryParseDecimal(HomePrice, out var hp) || hp <= 0)
        { ErrorMessage = L("Calc.RentVsBuy.Error.HomePrice", "房價格式錯誤"); HasResult = false; return; }
        if (!ParseHelpers.TryParseDecimal(DownPayment, out var dp) || dp < 0)
        { ErrorMessage = L("Calc.RentVsBuy.Error.DownPayment", "頭期款格式錯誤"); HasResult = false; return; }
        if (!ParseHelpers.TryParseDecimal(MortgageRatePercent, out var mRate) || mRate < 0)
        { ErrorMessage = L("Calc.RentVsBuy.Error.MortgageRate", "房貸利率格式錯誤"); HasResult = false; return; }
        if (!ParseHelpers.TryParseInt(LoanYears, out var ly) || ly <= 0)
        { ErrorMessage = L("Calc.RentVsBuy.Error.LoanYears", "貸款年數格式錯誤"); HasResult = false; return; }
        if (!ParseHelpers.TryParseDecimal(HoldingCostRatePercent, out var hc) || hc < 0)
        { ErrorMessage = L("Calc.RentVsBuy.Error.HoldingCost", "持有成本率格式錯誤"); HasResult = false; return; }
        if (!ParseHelpers.TryParseDecimal(AppreciationRatePercent, out var appr) || appr < 0)
        { ErrorMessage = L("Calc.RentVsBuy.Error.Appreciation", "增值率格式錯誤"); HasResult = false; return; }
        if (!ParseHelpers.TryParseDecimal(MonthlyRent, out var rent) || rent < 0)
        { ErrorMessage = L("Calc.RentVsBuy.Error.Rent", "月租格式錯誤"); HasResult = false; return; }
        if (!ParseHelpers.TryParseDecimal(RentIncreasePercent, out var ri) || ri < 0)
        { ErrorMessage = L("Calc.RentVsBuy.Error.RentIncrease", "租金漲幅格式錯誤"); HasResult = false; return; }
        if (!ParseHelpers.TryParseInt(CompareYears, out var cy) || cy <= 0)
        { ErrorMessage = L("Calc.RentVsBuy.Error.CompareYears", "比較年數格式錯誤"); HasResult = false; return; }
        if (!ParseHelpers.TryParseDecimal(InvestmentReturnPercent, out var inv) || inv < 0)
        { ErrorMessage = L("Calc.RentVsBuy.Error.InvestmentReturn", "投資報酬率格式錯誤"); HasResult = false; return; }
        if (!ParseHelpers.TryParseDecimal(PurchaseCostRatePercent, out var pc) || pc < 0)
        { ErrorMessage = L("Calc.RentVsBuy.Error.PurchaseCost", "買入成本率格式錯誤"); HasResult = false; return; }
        if (!ParseHelpers.TryParseDecimal(SellCostRatePercent, out var sc) || sc < 0)
        { ErrorMessage = L("Calc.RentVsBuy.Error.SellCost", "出售成本率格式錯誤"); HasResult = false; return; }

        var r = _svc.Calculate(new(hp, dp, mRate / 100m, ly, hc / 100m, appr / 100m, rent, ri / 100m, cy, inv / 100m, pc / 100m, sc / 100m));
        BuyerEndingNetWorth = r.BuyerEndingNetWorth.ToString("N0");
        RenterEndingNetWorth = r.RenterEndingNetWorth.ToString("N0");
        NetWorthDifference = r.NetWorthDifference.ToString("+#,0;-#,0;0");
        var absoluteDifference = Math.Abs(r.NetWorthDifference).ToString("N0");
        WinnerLabel = r.BuyCheaper
            ? L("Calc.RentVsBuy.Result.BuyCheaper", "買房期末淨值較高")
            : L("Calc.RentVsBuy.Result.RentCheaper", "租房期末淨值較高");
        ResultExplanation = r.BuyCheaper
            ? string.Format(L("Calc.RentVsBuy.Result.BuyExplanation", "比較 {0} 年後，買房期末淨值比租屋多 {1}。"), cy, absoluteDifference)
            : string.Format(L("Calc.RentVsBuy.Result.RentExplanation", "比較 {0} 年後，租屋並投資期末淨值比買房多 {1}。"), cy, absoluteDifference);
        BreakEvenYear = r.BreakEvenYear.HasValue
            ? string.Format(L("Calc.RentVsBuy.Result.BreakEvenFmt", "第 {0} 年"), r.BreakEvenYear.Value)
            : L("Calc.RentVsBuy.Result.NoBreakEven", "期間內未達損益兩平");
        BreakEvenExplanation = r.BreakEvenYear.HasValue
            ? string.Format(L("Calc.RentVsBuy.Result.BreakEvenExplanation", "第 {0} 年起，買房期末淨值首次追上或超過租屋。"), r.BreakEvenYear.Value)
            : string.Format(L("Calc.RentVsBuy.Result.NoBreakEvenExplanation", "在 {0} 年比較期間內，買房期末淨值尚未追上租屋並投資。"), cy);
        HasResult = true;
    }
}
