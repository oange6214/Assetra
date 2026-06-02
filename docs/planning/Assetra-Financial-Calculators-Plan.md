# 理財試算（Financial Calculators）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Assetra 新增「理財試算」功能（側欄一條入口、4 分頁：貸款計算／拿鐵因子／72 法則／租 vs 買），純試算、無 DB、零持倉依賴。

**Architecture:** 三層 —— Core `record` 模型 → Application 純計算 service（決定性、可單測）→ WPF 父子 ViewModel + `TabControl` View。導航接 `NavSection`/`MainViewModel`/`NavRailView`，比照 `MonteCarlo`/`Import` 既有模式。

**Tech Stack:** .NET 10、WPF、CommunityToolkit.Mvvm（`[ObservableProperty]`/`[RelayCommand]`）、xUnit、decimal 財務運算。

**Spec:** `docs/specs/2026-06-03-financial-calculators.md`

**注意：** 不要改 `docs/INDEX.md`（使用者 WIP）。每個 Task 完成即 commit。`TreatWarningsAsErrors` 開著，commit 前須 build 過。

---

## File Structure

| 檔案 | 責任 |
|---|---|
| `Assetra.Core/Models/Calculators/LoanAmortization.cs` | 貸款 input/row/schedule records |
| `Assetra.Core/Models/Calculators/LatteFactor.cs` | 拿鐵 input/result records + `LatteFrequency` enum |
| `Assetra.Core/Models/Calculators/RentVsBuy.cs` | 租vs買 input/result records |
| `Assetra.Application/Calculators/LoanAmortizationService.cs` | 攤還計算（RentVsBuy 也用） |
| `Assetra.Application/Calculators/LatteFactorCalculator.cs` | 年金 FV |
| `Assetra.Application/Calculators/RuleOf72Calculator.cs` | 72 法則雙向 |
| `Assetra.Application/Calculators/RentVsBuyCalculator.cs` | 租vs買淨成本 + 損益兩平 |
| `Assetra.WPF/Features/Calculators/CalculatorsViewModel.cs` | 父 VM，持 4 子 VM |
| `Assetra.WPF/Features/Calculators/{Loan,LatteFactor,RuleOf72,RentVsBuy}CalcViewModel.cs` | 4 子 VM |
| `Assetra.WPF/Features/Calculators/CalculatorsView.xaml(.cs)` | TabControl 殼 |
| `Assetra.WPF/Features/Calculators/{Loan,LatteFactor,RuleOf72,RentVsBuy}CalcView.xaml(.cs)` | 4 子 View |
| `Assetra.WPF/Infrastructure/CalculatorsServiceCollectionExtensions.cs` | `AddCalculatorsContext()` |
| 接線（改） | `Shell/NavSection.cs`、`Shell/MainViewModel.cs`、`Shell/NavRailViewModel.cs`、`Shell/NavRailView.xaml`、`Infrastructure/AppBootstrapper.cs` |
| 語言（改） | `Languages/zh-TW.xaml`、`Languages/en-US.xaml` |
| 測試 | `Assetra.Tests/Application/Calculators/*ServiceTests.cs`、`Assetra.Tests/WPF/CalculatorsViewModelTests.cs` |

決策說明：拿鐵/租vs買的複利 **不依賴** `GoalPlanningService`（它解的是「所需月投入/月數」等不同未知數）；FV/攤還在此各自直接寫，較簡潔解耦（spec 第 4 節允許「必要時抽共用 helper」）。

---

## Phase A — Math（Core + Application + 單測，TDD）

### Task A1：LoanAmortizationService

**Files:** Create `Assetra.Core/Models/Calculators/LoanAmortization.cs`、`Assetra.Application/Calculators/LoanAmortizationService.cs`；Test `Assetra.Tests/Application/Calculators/LoanAmortizationServiceTests.cs`

- [ ] **Step 1：寫失敗測試**
```csharp
using Assetra.Application.Calculators;
using Assetra.Core.Models.Calculators;
namespace Assetra.Tests.Application.Calculators;
public class LoanAmortizationServiceTests
{
    [Fact] // WHY: 攤還表的本金加總必須等於本金、末期餘額必須歸零，否則攤還邏輯錯誤
    public void Calculate_AmortizesToZero_PrincipalSumsToLoan()
    {
        var s = new LoanAmortizationService().Calculate(new(300_000m, 0.06m, 12));
        Assert.Equal(12, s.Rows.Count);
        Assert.Equal(0m, s.Rows[^1].EndBalance);
        Assert.Equal(300_000m, decimal.Round(s.Rows.Sum(r => r.Principal), 0));
        Assert.Equal(s.TotalPayment - 300_000m, decimal.Round(s.TotalInterest, 0));
    }
    [Fact] // WHY: 零利率必須退化為平均攤還、零利息
    public void Calculate_ZeroRate_SplitsEvenly()
    {
        var s = new LoanAmortizationService().Calculate(new(120_000m, 0m, 12));
        Assert.Equal(10_000m, s.MonthlyPayment);
        Assert.Equal(0m, s.TotalInterest);
    }
}
```
- [ ] **Step 2：跑測試確認 FAIL**　`dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "FullyQualifiedName~LoanAmortizationServiceTests"`　預期：編譯失敗（型別未定義）
- [ ] **Step 3：實作**
```csharp
// LoanAmortization.cs
namespace Assetra.Core.Models.Calculators;
public sealed record LoanAmortizationInputs(decimal Principal, decimal AnnualRate, int Months);
public sealed record LoanPaymentRow(int Month, decimal BeginBalance, decimal Payment, decimal Principal, decimal Interest, decimal EndBalance);
public sealed record LoanAmortizationSchedule(decimal MonthlyPayment, decimal TotalPayment, decimal TotalInterest, IReadOnlyList<LoanPaymentRow> Rows);
```
```csharp
// LoanAmortizationService.cs
using Assetra.Core.Models.Calculators;
namespace Assetra.Application.Calculators;
public sealed class LoanAmortizationService
{
    public LoanAmortizationSchedule Calculate(LoanAmortizationInputs i)
    {
        if (i.Principal <= 0) throw new ArgumentOutOfRangeException(nameof(i.Principal));
        if (i.AnnualRate < 0) throw new ArgumentOutOfRangeException(nameof(i.AnnualRate));
        if (i.Months <= 0) throw new ArgumentOutOfRangeException(nameof(i.Months));

        var r = i.AnnualRate / 12m;
        var n = i.Months;
        decimal pmt = r == 0m
            ? decimal.Round(i.Principal / n, 2)
            : decimal.Round(i.Principal * (r * Pow(1m + r, n)) / (Pow(1m + r, n) - 1m), 2);

        var rows = new List<LoanPaymentRow>(n);
        var balance = i.Principal;
        decimal totalInterest = 0m;
        for (int m = 1; m <= n; m++)
        {
            var interest = decimal.Round(balance * r, 2);
            var principalPart = pmt - interest;
            var begin = balance;
            balance -= principalPart;
            if (m == n) { principalPart += balance; balance = 0m; } // 末期吸收尾差、歸零
            rows.Add(new(m, begin, pmt, principalPart, interest, balance < 0 ? 0m : balance));
            totalInterest += interest;
        }
        return new(pmt, pmt * n, decimal.Round(totalInterest, 2), rows);
    }

    // 整數次方，全程 decimal 精度
    internal static decimal Pow(decimal baseValue, int exp)
    {
        decimal result = 1m;
        for (int k = 0; k < exp; k++) result *= baseValue;
        return result;
    }

    // 第 N 年底剩餘本金（RentVsBuy 用）
    public decimal RemainingBalanceAtMonth(LoanAmortizationInputs i, int month)
    {
        var s = Calculate(i);
        if (month <= 0) return i.Principal;
        if (month >= s.Rows.Count) return 0m;
        return s.Rows[month - 1].EndBalance;
    }
}
```
- [ ] **Step 4：跑測試確認 PASS**（同 Step 2 指令）
- [ ] **Step 5：commit**　`git add Assetra.Core/Models/Calculators/LoanAmortization.cs Assetra.Application/Calculators/LoanAmortizationService.cs Assetra.Tests/Application/Calculators/LoanAmortizationServiceTests.cs && git commit -m "feat(calc): 貸款攤還計算 service"`

### Task A2：LatteFactorCalculator

**Files:** Create `Assetra.Core/Models/Calculators/LatteFactor.cs`、`Assetra.Application/Calculators/LatteFactorCalculator.cs`；Test `.../LatteFactorCalculatorTests.cs`

- [ ] **Step 1：失敗測試**
```csharp
public class LatteFactorCalculatorTests
{
    [Fact] // WHY: 零報酬時複利後總值=純投入；多賺=0
    public void ZeroReturn_FvEqualsContributed()
    {
        var r = new LatteFactorCalculator().Calculate(new(100m, LatteFrequency.Monthly, 0m, 10));
        Assert.Equal(12_000m, r.TotalContributed);   // 100×120
        Assert.Equal(12_000m, r.FutureValue);
        Assert.Equal(0m, r.Gain);
    }
    [Fact] // WHY: 有報酬時 FV 須大於投入，且每日換算 ≈ ×365/12
    public void DailyWithReturn_GrowsAboveContributed()
    {
        var r = new LatteFactorCalculator().Calculate(new(50m, LatteFrequency.Daily, 0.06m, 20));
        Assert.True(r.FutureValue > r.TotalContributed);
        Assert.Equal(decimal.Round(50m*365m/12m*240m,0), r.TotalContributed);
    }
}
```
- [ ] **Step 2：FAIL**　`--filter "FullyQualifiedName~LatteFactorCalculatorTests"`
- [ ] **Step 3：實作**
```csharp
// LatteFactor.cs
namespace Assetra.Core.Models.Calculators;
public enum LatteFrequency { Daily, Weekly, Monthly }
public sealed record LatteFactorInputs(decimal AmountPerSpend, LatteFrequency Frequency, decimal AnnualReturn, int Years);
public sealed record LatteFactorResult(decimal TotalContributed, decimal FutureValue, decimal Gain);
```
```csharp
// LatteFactorCalculator.cs
using Assetra.Core.Models.Calculators;
namespace Assetra.Application.Calculators;
public sealed class LatteFactorCalculator
{
    public LatteFactorResult Calculate(LatteFactorInputs i)
    {
        if (i.AmountPerSpend < 0) throw new ArgumentOutOfRangeException(nameof(i.AmountPerSpend));
        if (i.Years <= 0) throw new ArgumentOutOfRangeException(nameof(i.Years));
        var monthly = i.Frequency switch
        {
            LatteFrequency.Daily => i.AmountPerSpend * 365m / 12m,
            LatteFrequency.Weekly => i.AmountPerSpend * 52m / 12m,
            _ => i.AmountPerSpend,
        };
        var r = i.AnnualReturn / 12m;
        var n = i.Years * 12;
        decimal fv = r == 0m ? monthly * n : monthly * (LoanAmortizationService.Pow(1m + r, n) - 1m) / r;
        var contributed = monthly * n;
        return new(decimal.Round(contributed, 0), decimal.Round(fv, 0), decimal.Round(fv - contributed, 0));
    }
}
```
- [ ] **Step 4：PASS** ・ **Step 5：commit** `feat(calc): 拿鐵因子年金 FV 計算`

### Task A3：RuleOf72Calculator

**Files:** Create `Assetra.Application/Calculators/RuleOf72Calculator.cs`；Test `.../RuleOf72CalculatorTests.cs`（此計算機無需 Core record）

- [ ] **Step 1：失敗測試**
```csharp
public class RuleOf72CalculatorTests
{
    [Fact] public void DoublingYears_From6Percent_Is12() => Assert.Equal(12.0, new RuleOf72Calculator().DoublingYears(6.0), 3);
    [Fact] public void RequiredRate_For8Years_Is9Percent() => Assert.Equal(9.0, new RuleOf72Calculator().RequiredRatePercent(8.0), 3);
    [Fact] public void NonPositiveRate_ReturnsInfinity() => Assert.True(double.IsInfinity(new RuleOf72Calculator().DoublingYears(0)));
}
```
- [ ] **Step 2：FAIL** ・ **Step 3：實作**
```csharp
namespace Assetra.Application.Calculators;
public sealed class RuleOf72Calculator
{
    public double DoublingYears(double annualRatePercent) => annualRatePercent <= 0 ? double.PositiveInfinity : 72.0 / annualRatePercent;
    public double RequiredRatePercent(double years) => years <= 0 ? double.PositiveInfinity : 72.0 / years;
}
```
- [ ] **Step 4：PASS** ・ **Step 5：commit** `feat(calc): 72 法則雙向計算`
（翻 4/8 倍 = 2×/3× DoublingYears，由 VM 組表，不入 service）

### Task A4：RentVsBuyCalculator（用 LoanAmortizationService）

**Files:** Create `Assetra.Core/Models/Calculators/RentVsBuy.cs`、`Assetra.Application/Calculators/RentVsBuyCalculator.cs`；Test `.../RentVsBuyCalculatorTests.cs`

- [ ] **Step 1：失敗測試**
```csharp
public class RentVsBuyCalculatorTests
{
    private static RentVsBuyCalculator New() => new(new LoanAmortizationService());

    [Fact] // WHY: 高增值時買應較划算且有損益兩平年
    public void HighAppreciation_BuyBecomesCheaper()
    {
        var r = New().Calculate(new(
            HomePrice: 10_000_000m, DownPayment: 2_000_000m, MortgageAnnualRate: 0.02m, LoanYears: 30,
            AnnualHoldingCostRate: 0.01m, AnnualAppreciation: 0.04m,
            MonthlyRent: 30_000m, AnnualRentIncrease: 0.02m, CompareYears: 30));
        Assert.True(r.BuyCheaper);
        Assert.NotNull(r.BreakEvenYear);
    }
    [Fact] // WHY: 租方 N 年淨成本 = 逐年遞增租金加總（漲幅 0 時 = 月租×12×N）
    public void ZeroRentIncrease_RentCostIsFlatSum()
    {
        var r = New().Calculate(new(8_000_000m, 1_600_000m, 0.02m, 30, 0.01m, 0m, 25_000m, 0m, 10));
        Assert.Equal(25_000m * 12 * 10, r.RentNetCost);
    }
}
```
- [ ] **Step 2：FAIL** ・ **Step 3：實作**
```csharp
// RentVsBuy.cs
namespace Assetra.Core.Models.Calculators;
public sealed record RentVsBuyInputs(
    decimal HomePrice, decimal DownPayment, decimal MortgageAnnualRate, int LoanYears,
    decimal AnnualHoldingCostRate, decimal AnnualAppreciation,
    decimal MonthlyRent, decimal AnnualRentIncrease, int CompareYears);
public sealed record RentVsBuyResult(decimal BuyNetCost, decimal RentNetCost, int? BreakEvenYear, bool BuyCheaper);
```
```csharp
// RentVsBuyCalculator.cs
using Assetra.Core.Models.Calculators;
namespace Assetra.Application.Calculators;
public sealed class RentVsBuyCalculator
{
    private readonly LoanAmortizationService _loan;
    public RentVsBuyCalculator(LoanAmortizationService loan) => _loan = loan;

    public RentVsBuyResult Calculate(RentVsBuyInputs i)
    {
        if (i.HomePrice <= 0 || i.CompareYears <= 0) throw new ArgumentOutOfRangeException();
        var loanAmount = i.HomePrice - i.DownPayment;
        var loanInputs = new LoanAmortizationInputs(loanAmount <= 0 ? 1m : loanAmount, i.MortgageAnnualRate, i.LoanYears * 12);
        var monthlyPayment = loanAmount <= 0 ? 0m : _loan.Calculate(loanInputs).MonthlyPayment;

        int? breakEven = null;
        decimal buyAtN = 0m, rentAtN = 0m;
        for (int year = 1; year <= i.CompareYears; year++)
        {
            // 買方累積現金支出
            var monthsPaid = Math.Min(year, i.LoanYears) * 12;
            var mortgagePaid = monthlyPayment * monthsPaid;
            var holding = i.HomePrice * i.AnnualHoldingCostRate * year;
            var cashOut = i.DownPayment + mortgagePaid + holding;
            // 期末房產淨值
            var homeValue = i.HomePrice * LoanAmortizationService.Pow(1m + i.AnnualAppreciation, year);
            var remaining = loanAmount <= 0 ? 0m : _loan.RemainingBalanceAtMonth(loanInputs, year * 12);
            var equity = homeValue - remaining;
            var buyNet = cashOut - equity;
            // 租方累積（逐年遞增）
            decimal rentNet = 0m;
            for (int k = 0; k < year; k++)
                rentNet += i.MonthlyRent * 12m * LoanAmortizationService.Pow(1m + i.AnnualRentIncrease, k);

            if (breakEven is null && buyNet <= rentNet) breakEven = year;
            if (year == i.CompareYears) { buyAtN = buyNet; rentAtN = rentNet; }
        }
        return new(decimal.Round(buyAtN, 0), decimal.Round(rentAtN, 0), breakEven, buyAtN <= rentAtN);
    }
}
```
- [ ] **Step 4：PASS** ・ **Step 5：commit** `feat(calc): 租vs買淨成本與損益兩平計算`

---

## Phase B — ViewModels（TDD）

模式比照 `Assetra.WPF/Features/MonteCarlo/MonteCarloViewModel.cs`：`ObservableObject`、輸入為 `string` 的 `[ObservableProperty]`、`[RelayCommand] Calculate`、`ParseHelpers.TryParse*` 驗證失敗設雙語 `ErrorMessage` 且 `HasResult=false`、結果以屬性 + `ReadOnlyObservableCollection`（表格）暴露、可選注入 `ILocalizationService`（`L(key, fallback)`）。

### Task B1–B4：四個子 VM
- [ ] **每個 VM 一個 Task**，各含：輸入屬性、`Calculate` 指令、驗證、結果屬性/集合。
- [ ] **測試** `Assetra.Tests/WPF/CalculatorsViewModelTests.cs`，每個 VM 至少二測：
  - 壞輸入（非數字/負值）→ `ErrorMessage != null` 且 `HasResult == false`
  - 正常輸入 → `HasResult == true` 且關鍵輸出數值正確（對標 service 結果）
- [ ] 每個 VM 完成即 commit：`feat(calc): {名稱}計算機 ViewModel`

範例（LoanCalcViewModel 骨架）：
```csharp
public sealed partial class LoanCalcViewModel : ObservableObject
{
    private readonly LoanAmortizationService _svc;
    [ObservableProperty] private string _principal = "3000000";
    [ObservableProperty] private string _annualRatePercent = "2.0";
    [ObservableProperty] private string _months = "360";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _monthlyPayment = "";
    [ObservableProperty] private string _totalInterest = "";
    private readonly ObservableCollection<LoanPaymentRow> _schedule = [];
    public ReadOnlyObservableCollection<LoanPaymentRow> Schedule { get; }
    public LoanCalcViewModel(LoanAmortizationService svc) { _svc = svc; Schedule = new(_schedule); }

    [RelayCommand] private void Calculate()
    {
        ErrorMessage = null;
        if (!ParseHelpers.TryParseDecimal(Principal, out var p) || p <= 0) { ErrorMessage = "本金格式錯誤"; HasResult = false; return; }
        if (!ParseHelpers.TryParseDecimal(AnnualRatePercent, out var rate) || rate < 0) { ErrorMessage = "利率格式錯誤"; HasResult = false; return; }
        if (!ParseHelpers.TryParseInt(Months, out var n) || n <= 0) { ErrorMessage = "期數格式錯誤"; HasResult = false; return; }
        var s = _svc.Calculate(new(p, rate / 100m, n));
        MonthlyPayment = s.MonthlyPayment.ToString("N0");
        TotalInterest = s.TotalInterest.ToString("N0");
        _schedule.Clear(); foreach (var row in s.Rows) _schedule.Add(row);
        HasResult = true;
    }
}
```

### Task B5：CalculatorsViewModel（父）
- [ ] 建構式注入 4 子 VM，暴露為屬性 `Loan/Latte/Rule72/RentVsBuy`。commit `feat(calc): 理財試算父 ViewModel`

---

## Phase C — Views（XAML，照 `MonteCarloView.xaml` 版型）

- [ ] **Task C1–C4：四個子 View**。每個：表單區（`UniformGrid` + `FormField` 樣式；金額欄 `behaviors:ThousandSeparatorBehavior.IsEnabled="True"`；綁定 `UpdateSourceTrigger=PropertyChanged`）→ `Calculate` 按鈕 → 結果卡片；貸款/租vs買加 `DataGrid` 綁 `Schedule`/逐年。所有文字用 `{DynamicResource Calc.*}`。錯誤用 `ErrorMessage` + `InverseBooleanToVisibilityConverter`/`HasResult`。
- [ ] **Task C5：CalculatorsView**＝`TabControl`，4 個 `TabItem`（Header 用 `Calc.Tab.*`），各 `TabItem` 內容綁子 VM（`DataContext="{Binding Loan}"` 等）。比照 `NavRailView.xaml` 的 `ImportContentTemplate`（TabControl）。
- [ ] commit `feat(calc): 理財試算 4 分頁 View`

---

## Phase D — Wiring（DI + 導航，照探查的標準 9 步）

**Task D1：** 依序改下列檔（每處皆有 MonteCarlo 對照）：
- [ ] `Shell/NavSection.cs`：enum 加 `Calculators`
- [ ] `Infrastructure/CalculatorsServiceCollectionExtensions.cs`（新）：`AddCalculatorsContext()` 註冊 4 service（`LoanAmortizationService` 等）+ 5 個 VM（含父）為 `AddSingleton`
- [ ] `Infrastructure/AppBootstrapper.cs`：`.AddCalculatorsContext()`
- [ ] `Shell/MainViewModel.cs`：建構式注入 + `public CalculatorsViewModel Calculators { get; }`
- [ ] `Shell/NavRailViewModel.cs` `BuildGroups()`：在「規劃」群組加 `NavLeafVm { Section = NavSection.Calculators, LabelResourceKey = "Calc.Title", IconSymbol = "Calculator24", ToolTipResourceKey = "Calc.Title" }`
- [ ] `Shell/NavRailView.xaml`：加 `xmlns:calc`、`CalculatorsContentTemplate`（`<calc:CalculatorsView DataContext="{Binding Calculators}"/>`）、`ActiveSection == NavSection.Calculators` 的 `DataTrigger`
- [ ] build：`dotnet build Assetra.slnx`；手動起 app 確認側欄出現「理財試算」、4 分頁可切換
- [ ] commit `feat(calc): 理財試算導航與 DI 接線`

---

## Phase E — Localization

**Task E1：** 在 `Languages/zh-TW.xaml` 與 `Languages/en-US.xaml` **同步**新增 `Calc.*`：
- [ ] `Calc.Title`、`Calc.Tab.{Loan|Latte|Rule72|RentVsBuy}`
- [ ] 各計算機 `Calc.{X}.Input.*`、`Calc.{X}.Result.*`、`Calc.{X}.Error.*`（與 VM 內 fallback 字串對應）
- [ ] build 確認無缺 key；commit `feat(calc): 理財試算雙語字串`
- [ ] ⚠️ **不要動 `docs/INDEX.md`**（使用者 WIP）

---

## Self-Review

- **Spec 覆蓋**：A1–A4 = spec §4 四個計算機數學；B = §5 VM/驗證；C = View；D = §3 導航接線；E = §7 在地化；驗收 §8 由 D 的手動確認 + 各測試覆蓋。✅ 無遺漏。
- **Placeholder 掃描**：數學層全程實碼；UI/wiring 為「照 MonteCarlo/Import 模板 + 明確步驟」（刻意，避免 5000 行重抄模板、且符合 Rule 6 預算）—— 非 TBD，執行者有對照檔可循。
- **型別一致**：`LoanAmortizationInputs/Schedule/Row`、`LatteFactor*`、`RentVsBuy*`、`LatteFrequency`、`LoanAmortizationService.Pow/RemainingBalanceAtMonth` 跨 Task 命名一致；A2/A4 重用 A1 的 `Pow`。
- **風險**：`RemainingBalanceAtMonth` 重算整張表（O(n)）——比較年數 ≤ 30 可接受；若慢再快取。`Pow` 為整數次方迴圈、decimal 精度安全。
