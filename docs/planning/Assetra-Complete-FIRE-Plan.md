# Assetra Complete FIRE Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the current FIRE calculator into a complete financial independence planning module with saved scenarios, clear assumptions, deterministic projections, retirement drawdown, optional Monte Carlo confidence, and Assetra net-worth / goals integration.

**Architecture:** Keep pure FIRE calculation in `Assetra.Application.Fire`, contracts and immutable models in `Assetra.Core`, persistence in `Assetra.Infrastructure`, and WPF orchestration in `Assetra.WPF.Features.Fire`. Preserve the current simple calculator as Basic mode, then add scenario-based Advanced mode through new services instead of overloading the existing `FireInputs`.

**Tech Stack:** .NET 10, WPF MVVM, CommunityToolkit.Mvvm, SQLite repositories, xUnit, Moq.

---

## Current State

Assetra currently has a useful but incomplete FIRE implementation:

- `Assetra.Core/Models/Fire/FireInputs.cs`
  - Inputs: current net worth, annual expenses, annual savings, expected annual return, withdrawal rate, max years.
- `Assetra.Core/Models/Fire/FireProjection.cs`
  - Outputs: FIRE number, years to FIRE, projected net worth at FIRE, wealth path.
- `Assetra.Application/Fire/FireCalculatorService.cs`
  - Formula: `AnnualExpenses / WithdrawalRate`.
  - Projection: yearly compounding plus annual savings until `MaxYears`.
- `Assetra.WPF/Features/Fire/FireViewModel.cs`
  - Manual inputs, calculate command, sync-to-goals command.
  - Current net worth can be filled from the app net worth provider.
  - Annual expense and annual savings now show monthly helper text.
- `Assetra.Tests/Application/Fire/FireCalculatorServiceTests.cs`
  - Covers current basic formula and validation.
- `Assetra.Tests/WPF/FireViewModelTests.cs`
  - Covers WPF parsing, command behavior, and goals sync behavior.

The current feature is therefore a "FIRE number calculator". A complete FIRE module needs scenario persistence, assumption clarity, retirement drawdown, current net-worth source control, and richer outputs.

---

## Product Definition

### What "Complete FIRE" Means In Assetra

A complete FIRE module should answer these user questions:

1. Based on my actual Assetra net worth, when can I become financially independent?
2. How much invested wealth do I need if my annual expenses are X?
3. If I save Y per year and earn Z return, what year do I reach the target?
4. After reaching FIRE, will the portfolio last until my planned life expectancy?
5. Which assumptions created this result?
6. Can I compare multiple plans, for example conservative / base / aggressive?
7. Can I turn the plan into a financial goal and keep progress synced?

### Modes

The UI should support two modes:

- **Basic mode**
  - Keeps the current user mental model.
  - Inputs: current net worth, annual expenses, annual savings, expected annual return, safe withdrawal rate.
  - Output: required assets, years to FIRE, projected net worth at FIRE, wealth path.
  - This mode must remain simple and should be the first view for most users.

- **Advanced mode**
  - Adds saved scenarios and explicit assumptions.
  - Inputs: return mode, inflation, spending growth, savings growth, retirement spending, current age, life expectancy, one-time events.
  - Outputs: accumulation path, drawdown path, monthly savings gap, success probability, warnings.

### Terminology

Use wording that explains the formula instead of implying the number is user-entered:

- Replace `FIRE 目標金額` with `財務自由所需資產`.
- Show formula helper: `年支出 ÷ 安全提領率`.
- Use `達成時淨資產` for projected net worth at FIRE.
- Use `所需年數` and `預估達成年份`.
- Use `實質報酬率` only when inflation is already removed.
- Use `名目報酬率` only when inflation is modeled separately.

### Default Assumptions

Use conservative defaults that are visible, editable, and saved with the scenario:

- Annual expenses: user input.
- Annual savings: user input.
- Safe withdrawal rate: `0.04`.
- Expected annual return: `0.05`.
- Inflation rate: `0.02` in Advanced mode.
- Life expectancy age: `90`.
- Current age: empty until user fills it.
- Return mode: `Real` in Basic mode to avoid double-counting inflation.

---

## File Map

### Core Models

- Create: `Assetra.Core/Models/Fire/FireScenario.cs`
  - Saved scenario aggregate used by Application and WPF.
- Create: `Assetra.Core/Models/Fire/FireCashFlowEvent.cs`
  - One-time or recurring inflow/outflow assumptions.
- Create: `Assetra.Core/Models/Fire/FirePlanningInputs.cs`
  - Rich calculation input for Advanced mode.
- Create: `Assetra.Core/Models/Fire/FirePlanningProjection.cs`
  - Rich deterministic result.
- Create: `Assetra.Core/Models/Fire/FireDrawdownPoint.cs`
  - Retirement drawdown yearly row.
- Create: `Assetra.Core/Models/Fire/FireProjectionWarning.cs`
  - Typed warning for invalid or risky assumptions.
- Modify: `Assetra.Core/Models/Fire/FireInputs.cs`
  - Keep unchanged for Basic mode compatibility unless a non-breaking display-only field is required.
- Modify: `Assetra.Core/Models/Fire/FireProjection.cs`
  - Keep unchanged for Basic mode compatibility.

### Core Interfaces

- Create: `Assetra.Core/Interfaces/Fire/IFirePlanningService.cs`
  - Scenario-based deterministic calculation.
- Create: `Assetra.Core/Interfaces/Fire/IFireDrawdownService.cs`
  - Retirement drawdown calculation.
- Create: `Assetra.Core/Interfaces/Fire/IFireScenarioRepository.cs`
  - Persistence contract for scenarios and events.
- Create: `Assetra.Core/Interfaces/Fire/IFireMonteCarloService.cs`
  - Optional stochastic success probability service.
- Keep: `Assetra.Core/Interfaces/Fire/IFireCalculatorService.cs`
  - Basic calculator compatibility interface.

### Application Services

- Create: `Assetra.Application/Fire/FirePlanningService.cs`
  - Advanced deterministic projection.
- Create: `Assetra.Application/Fire/FireDrawdownService.cs`
  - Post-FIRE drawdown path.
- Create: `Assetra.Application/Fire/FireMonteCarloService.cs`
  - Monte Carlo confidence after deterministic engine is stable.
- Modify: `Assetra.Application/Fire/FireCalculatorService.cs`
  - Keep current behavior stable; only factor shared helpers if tests lock the behavior first.

### Infrastructure

- Create: `Assetra.Infrastructure/Persistence/FireScenarioSqliteRepository.cs`
  - Saves scenarios and cash-flow events.
- Modify: SQLite schema/migration files currently used by Assetra.
  - Add `fire_scenario`.
  - Add `fire_cash_flow_event`.
  - Add indexes for default scenario and updated timestamp.

### WPF

- Modify: `Assetra.WPF/Features/Fire/FireViewModel.cs`
  - Split Basic input state from scenario state.
  - Add scenario selector, mode toggle, save/duplicate/delete scenario commands.
- Modify: `Assetra.WPF/Features/Fire/FireView.xaml`
  - Add Basic / Advanced mode layout.
  - Add scenario toolbar.
  - Add result sections and assumption explanations.
- Modify: `Assetra.WPF/Features/Fire/FireView.xaml.cs`
  - Keep only view lifecycle warming; no calculation logic.
- Modify: `Assetra.WPF/Infrastructure/FireServiceCollectionExtensions.cs`
  - Register new services and repository.
- Modify localization dictionaries under `Assetra.WPF/Resources`.
  - Add labels, helpers, warnings, scenario actions, and validation messages.

### Tests

- Create: `Assetra.Tests/Application/Fire/FirePlanningServiceTests.cs`
- Create: `Assetra.Tests/Application/Fire/FireDrawdownServiceTests.cs`
- Create: `Assetra.Tests/Infrastructure/FireScenarioSqliteRepositoryTests.cs`
- Modify: `Assetra.Tests/Application/Fire/FireCalculatorServiceTests.cs`
- Modify: `Assetra.Tests/WPF/FireViewModelTests.cs`

### Docs

- Modify: `docs/INDEX.md`
- Modify: `docs/releases/CHANGELOG.md` when implementation is complete.
- Create or update a FIRE user guide if the UI adds saved scenarios:
  - `docs/guides/FIRE-Planning.md`

---

## Domain Model Draft

These shapes are the intended contracts. Implement them with records and enums in `Assetra.Core`.

```csharp
namespace Assetra.Core.Models.Fire;

public enum FireScenarioMode
{
    Basic = 0,
    Advanced = 1,
}

public enum FireNetWorthSource
{
    Manual = 0,
    AppNetWorth = 1,
    PortfolioGroup = 2,
}

public enum FireReturnMode
{
    Real = 0,
    Nominal = 1,
}

public sealed record FireScenario(
    Guid Id,
    string Name,
    FireScenarioMode Mode,
    FireNetWorthSource NetWorthSource,
    Guid? PortfolioGroupId,
    decimal? CurrentNetWorthOverride,
    decimal AnnualExpenses,
    decimal AnnualSavings,
    decimal ExpectedAnnualReturn,
    FireReturnMode ReturnMode,
    decimal? InflationRate,
    decimal? SavingsGrowthRate,
    decimal? ExpenseGrowthRate,
    decimal WithdrawalRate,
    int? CurrentAge,
    int? LifeExpectancyAge,
    decimal? RetirementAnnualExpenses,
    decimal? CustomTargetAmount,
    bool IncludeTaxes,
    string? Notes,
    bool IsDefault,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
```

```csharp
namespace Assetra.Core.Models.Fire;

public enum FireCashFlowDirection
{
    Inflow = 0,
    Outflow = 1,
}

public enum FireCashFlowGrowthMode
{
    Fixed = 0,
    InflationAdjusted = 1,
    CustomGrowthRate = 2,
}

public sealed record FireCashFlowEvent(
    Guid Id,
    Guid ScenarioId,
    string Name,
    int StartYearOffset,
    int? EndYearOffset,
    decimal AnnualAmount,
    FireCashFlowDirection Direction,
    FireCashFlowGrowthMode GrowthMode,
    decimal? CustomGrowthRate,
    string? Notes);
```

```csharp
namespace Assetra.Core.Models.Fire;

public sealed record FirePlanningProjection(
    decimal RequiredAssets,
    int? YearsToFire,
    int? FireYear,
    decimal ProjectedNetWorthAtFire,
    decimal RequiredMonthlySavings,
    decimal? MonteCarloSuccessRate,
    IReadOnlyList<FireWealthPoint> AccumulationPath,
    IReadOnlyList<FireDrawdownPoint> DrawdownPath,
    IReadOnlyList<FireProjectionWarning> Warnings);
```

---

## Calculation Rules

### Required Assets

Default required assets:

```text
required_assets = annual_retirement_expenses / withdrawal_rate
```

Rules:

- In Basic mode, `annual_retirement_expenses = annual_expenses`.
- In Advanced mode, if `RetirementAnnualExpenses` is set, use it.
- If `CustomTargetAmount` is set, use that as an explicit override and show `自訂目標`.
- `WithdrawalRate` must be greater than `0` and less than or equal to `1`.

### Real vs Nominal Return

The service must not silently mix real and nominal assumptions.

- `FireReturnMode.Real`
  - `ExpectedAnnualReturn` is already inflation-adjusted.
  - Spending and savings stay in today's purchasing power unless growth rates are explicitly set.
- `FireReturnMode.Nominal`
  - `ExpectedAnnualReturn` is nominal.
  - `InflationRate` must be set.
  - Expenses inflate by `InflationRate` unless `ExpenseGrowthRate` is set.

### Accumulation Path

Year 0 starts with current net worth.

```text
balance_next =
    balance_current * (1 + return_rate)
  + annual_savings_for_year
  + inflow_events_for_year
  - outflow_events_for_year
```

For a negative or impossible path:

- Keep projecting until `MaxYears`.
- Return `YearsToFire = null`.
- Add warning `UnableToReachFireWithinProjection`.

### Retirement Drawdown

Drawdown begins at the FIRE year if reached, otherwise it can still be simulated from current balance in Advanced mode for "what if I retire now".

```text
ending_balance =
    starting_balance * (1 + return_rate)
  - annual_retirement_expenses_for_year
  + retirement_inflows_for_year
  - retirement_outflows_for_year
```

Warnings:

- `DrawdownDepletesBeforeLifeExpectancy`
- `WithdrawalRateAboveCommonRange`
- `AnnualSavingsBelowZero`
- `InflationMissingForNominalMode`

---

## UX Design

### Page Structure

The FIRE page should be a planning surface, not only a form.

1. Header
   - Title: `FIRE`
   - Scenario selector.
   - Actions: `新增情境`, `複製`, `設為預設`, `刪除`.
2. Assumption panel
   - Basic / Advanced segmented control.
   - Basic inputs first.
   - Advanced assumptions collapsed by default.
3. Result summary
   - `財務自由所需資產`
   - `所需年數`
   - `預估達成年份`
   - `達成時淨資產`
   - `完成進度`
   - `每月需增加儲蓄`
4. Projection area
   - Accumulation path chart/list.
   - Drawdown path chart/list in Advanced mode.
5. Scenario comparison
   - Compare at most three scenarios at once.
6. Integration footer
   - Sync to financial goal.
   - Explain what is synced.

### Basic Input Layout

Keep the first screen small:

- `目前淨資產`
  - Primary action: `使用目前財務概覽淨資產`.
  - Secondary source: portfolio group if group service exists.
- `年支出`
  - Helper: `600,000 ÷ 12 = 每月 50,000`.
- `年儲蓄`
  - Helper: `300,000 ÷ 12 = 每月 25,000`.
- `預期年化報酬率`
  - Helper: `Basic 模式使用實質報酬率，也就是已扣除通膨後的報酬。`
- `安全提領率`
  - Helper: `4% 對應 25 倍年支出。`

### Advanced Input Layout

Group advanced controls by meaning:

- `資產來源`
  - Manual
  - App net worth
  - Portfolio group
- `報酬與通膨`
  - Real / Nominal return mode.
  - Expected return.
  - Inflation rate when nominal.
- `支出與儲蓄成長`
  - Spending growth.
  - Savings growth.
  - Retirement spending.
- `人生階段`
  - Current age.
  - Life expectancy.
- `一次性事件`
  - Add income or expense events.

### Empty State

Before calculation:

- Icon: calculator or target.
- Title: `尚未計算 FIRE`
- Body: `填入目前淨資產、年支出與年儲蓄，系統會估算財務自由所需資產與達成年份。`
- Primary action: `計算`

### Validation Style

Validation must explain the business rule:

- `年支出必須大於 0，因為 FIRE 目標由年支出推算。`
- `安全提領率必須介於 0% 到 100% 之間。`
- `名目報酬模式需要通膨率，否則會高估退休後購買力。`
- `目前年齡不可大於或等於預期壽命。`

---

## Implementation Tasks

### Task 1: Lock Basic Calculator Behavior

**Files:**

- Modify: `Assetra.Tests/Application/Fire/FireCalculatorServiceTests.cs`
- Read: `Assetra.Application/Fire/FireCalculatorService.cs`
- Read: `Assetra.Core/Models/Fire/FireInputs.cs`

- [x] Add a regression test that proves Basic mode remains unchanged.

```csharp
[Fact]
public void Calculate_BasicMode_UsesAnnualExpensesDividedByWithdrawalRate()
{
    var service = new FireCalculatorService();

    var result = service.Calculate(new FireInputs(
        CurrentNetWorth: 1_000_000m,
        AnnualExpenses: 600_000m,
        AnnualSavings: 300_000m,
        ExpectedAnnualReturn: 0.05m,
        WithdrawalRate: 0.04m));

    Assert.Equal(15_000_000m, result.FireNumber);
}
```

- [x] Run:

```powershell
dotnet test Assetra.Tests\Assetra.Tests.csproj --filter FullyQualifiedName~Assetra.Tests.Application.Fire
```

Expected: all FIRE application tests pass.

- [ ] Commit:

```powershell
git add Assetra.Tests\Application\Fire\FireCalculatorServiceTests.cs
git commit -m "test: lock basic FIRE calculator behavior"
```

### Task 2: Add Advanced FIRE Models

**Files:**

- Create: `Assetra.Core/Models/Fire/FireScenario.cs`
- Create: `Assetra.Core/Models/Fire/FireCashFlowEvent.cs`
- Create: `Assetra.Core/Models/Fire/FirePlanningProjection.cs`
- Create: `Assetra.Core/Models/Fire/FireProjectionWarning.cs`

- [x] Add the enums and records from the Domain Model Draft.
- [x] Ensure all records are immutable.
- [x] Keep `FireInputs` and `FireProjection` unchanged.
- [x] Run:

```powershell
dotnet build Assetra.slnx
```

Expected: build succeeds.

- [ ] Commit:

```powershell
git add Assetra.Core\Models\Fire
git commit -m "feat: add FIRE planning models"
```

### Task 3: Add Deterministic Planning Service

**Files:**

- Create: `Assetra.Core/Interfaces/Fire/IFirePlanningService.cs`
- Create: `Assetra.Application/Fire/FirePlanningService.cs`
- Create: `Assetra.Tests/Application/Fire/FirePlanningServiceTests.cs`

- [x] Write a test for Basic-equivalent Advanced projection.

```csharp
[Fact]
public void Project_BasicEquivalentScenario_MatchesRequiredAssetsFormula()
{
    var service = new FirePlanningService();
    var scenario = FireScenarioFactory.Basic(
        currentNetWorth: 1_000_000m,
        annualExpenses: 600_000m,
        annualSavings: 300_000m,
        expectedAnnualReturn: 0.05m,
        withdrawalRate: 0.04m);

    var result = service.Project(scenario, Array.Empty<FireCashFlowEvent>(), currentYear: 2026);

    Assert.Equal(15_000_000m, result.RequiredAssets);
    Assert.NotEmpty(result.AccumulationPath);
}
```

- [x] Write a test for nominal mode requiring inflation.

```csharp
[Fact]
public void Project_NominalModeWithoutInflation_ReturnsWarning()
{
    var service = new FirePlanningService();
    var scenario = FireScenarioFactory.Advanced(returnMode: FireReturnMode.Nominal, inflationRate: null);

    var result = service.Project(scenario, Array.Empty<FireCashFlowEvent>(), currentYear: 2026);

    Assert.Contains(result.Warnings, w => w.Code == FireProjectionWarningCode.InflationMissingForNominalMode);
}
```

- [x] Implement the smallest deterministic projection that passes the tests.
- [x] Run:

```powershell
dotnet test Assetra.Tests\Assetra.Tests.csproj --filter FullyQualifiedName~Assetra.Tests.Application.Fire.FirePlanningServiceTests
```

Expected: planning service tests pass.

- [ ] Commit:

```powershell
git add Assetra.Core\Interfaces\Fire Assetra.Application\Fire Assetra.Tests\Application\Fire
git commit -m "feat: add deterministic FIRE planning service"
```

### Task 4: Add Drawdown Service

**Files:**

- Create: `Assetra.Core/Interfaces/Fire/IFireDrawdownService.cs`
- Create: `Assetra.Application/Fire/FireDrawdownService.cs`
- Create: `Assetra.Tests/Application/Fire/FireDrawdownServiceTests.cs`

- [x] Write a test that a portfolio survives to life expectancy.

```csharp
[Fact]
public void ProjectDrawdown_WhenBalanceLastsToLifeExpectancy_DoesNotWarn()
{
    var service = new FireDrawdownService();

    var result = service.ProjectDrawdown(
        startingBalance: 20_000_000m,
        annualRetirementExpenses: 600_000m,
        expectedAnnualReturn: 0.04m,
        currentAge: 45,
        lifeExpectancyAge: 90);

    Assert.DoesNotContain(result.Warnings, w => w.Code == FireProjectionWarningCode.DrawdownDepletesBeforeLifeExpectancy);
}
```

- [x] Write a test that a portfolio depletion warning is returned.
- [x] Implement yearly drawdown points.
- [x] Run:

```powershell
dotnet test Assetra.Tests\Assetra.Tests.csproj --filter FullyQualifiedName~Assetra.Tests.Application.Fire.FireDrawdownServiceTests
```

Expected: drawdown tests pass.

- [ ] Commit:

```powershell
git add Assetra.Core\Interfaces\Fire Assetra.Application\Fire Assetra.Tests\Application\Fire
git commit -m "feat: add FIRE drawdown projection"
```

### Task 5: Add Scenario Persistence

**Files:**

- Create: `Assetra.Core/Interfaces/Fire/IFireScenarioRepository.cs`
- Create: `Assetra.Infrastructure/Persistence/FireScenarioSqliteRepository.cs`
- Modify: Assetra SQLite schema/migration file currently responsible for app tables.
- Create: `Assetra.Tests/Infrastructure/FireScenarioSqliteRepositoryTests.cs`

- [x] Add schema:

```sql
CREATE TABLE IF NOT EXISTS fire_scenario (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    mode INTEGER NOT NULL,
    net_worth_source INTEGER NOT NULL,
    portfolio_group_id TEXT NULL,
    current_net_worth_override TEXT NULL,
    annual_expenses TEXT NOT NULL,
    annual_savings TEXT NOT NULL,
    expected_annual_return TEXT NOT NULL,
    return_mode INTEGER NOT NULL,
    inflation_rate TEXT NULL,
    savings_growth_rate TEXT NULL,
    expense_growth_rate TEXT NULL,
    withdrawal_rate TEXT NOT NULL,
    current_age INTEGER NULL,
    life_expectancy_age INTEGER NULL,
    retirement_annual_expenses TEXT NULL,
    custom_target_amount TEXT NULL,
    include_taxes INTEGER NOT NULL,
    notes TEXT NULL,
    is_default INTEGER NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
```

```sql
CREATE TABLE IF NOT EXISTS fire_cash_flow_event (
    id TEXT PRIMARY KEY,
    scenario_id TEXT NOT NULL,
    name TEXT NOT NULL,
    start_year_offset INTEGER NOT NULL,
    end_year_offset INTEGER NULL,
    annual_amount TEXT NOT NULL,
    direction INTEGER NOT NULL,
    growth_mode INTEGER NOT NULL,
    custom_growth_rate TEXT NULL,
    notes TEXT NULL,
    FOREIGN KEY (scenario_id) REFERENCES fire_scenario(id) ON DELETE CASCADE
);
```

- [x] Repository test: save default scenario and load it after reopening the database.
- [x] Repository test: replacing the default scenario clears previous `IsDefault`.
- [x] Repository test: deleting a scenario cascades its cash-flow events.
- [x] Run:

```powershell
dotnet test Assetra.Tests\Assetra.Tests.csproj --filter FullyQualifiedName~FireScenarioSqliteRepositoryTests
```

Expected: persistence tests pass.

- [ ] Commit:

```powershell
git add Assetra.Core\Interfaces\Fire Assetra.Infrastructure Assetra.Tests\Infrastructure
git commit -m "feat: persist FIRE scenarios"
```

### Task 6: Refactor FireViewModel Around Scenarios

**Files:**

- Modify: `Assetra.WPF/Features/Fire/FireViewModel.cs`
- Modify: `Assetra.WPF/Infrastructure/FireServiceCollectionExtensions.cs`
- Modify: `Assetra.Tests/WPF/FireViewModelTests.cs`

- [x] Add constructor dependencies:

```csharp
IFirePlanningService planningService,
IFireScenarioRepository scenarioRepository,
IFireDrawdownService drawdownService
```

- [x] Keep existing `IFireCalculatorService` dependency until Basic compatibility is fully covered.
- [x] Add state:

```csharp
ObservableCollection<FireScenarioRowViewModel> Scenarios
FireScenarioRowViewModel? SelectedScenario
bool IsAdvancedMode
FirePlanningProjection? PlanningResult
```

- [x] Add commands:

```csharp
LoadScenariosAsyncCommand
CreateScenarioAsyncCommand
SaveScenarioAsyncCommand
DuplicateScenarioAsyncCommand
DeleteScenarioAsyncCommand
SetDefaultScenarioAsyncCommand
CalculatePlanningAsyncCommand
```

- [x] Test: default scenario loads automatically.
- [x] Test: app net worth source fills current net worth but manual override is preserved.
- [x] Test: switching Basic/Advanced does not clear saved scenario data.
- [x] Run:

```powershell
dotnet test Assetra.Tests\Assetra.Tests.csproj --filter FullyQualifiedName~Assetra.Tests.WPF.FireViewModelTests
```

Expected: WPF FIRE VM tests pass.

- [ ] Commit:

```powershell
git add Assetra.WPF\Features\Fire Assetra.WPF\Infrastructure\FireServiceCollectionExtensions.cs Assetra.Tests\WPF\FireViewModelTests.cs
git commit -m "feat: add FIRE scenario view model"
```

### Task 7: Rebuild FIRE XAML UX

**Files:**

- Modify: `Assetra.WPF/Features/Fire/FireView.xaml`
- Modify: localization dictionaries under `Assetra.WPF/Resources`

- [x] Add scenario toolbar at the top.
- [x] Add Basic / Advanced segmented control.
- [x] Keep Basic mode compact.
- [x] Place Advanced assumptions behind grouped sections.
- [x] Rename `FIRE 目標金額` display to `財務自由所需資產`.
- [x] Add formula helper near the result:

```text
年支出 ÷ 安全提領率 = 財務自由所需資產
```

- [x] Add drawdown result section in Advanced mode.
- [x] Add warnings panel only when warnings exist.
- [x] Run:

```powershell
dotnet build Assetra.slnx
```

Expected: build succeeds and XAML loads without StaticResource or binding errors.

- [ ] Commit:

```powershell
git add Assetra.WPF\Features\Fire Assetra.WPF\Resources
git commit -m "feat: redesign FIRE planning view"
```

### Task 8: Add Monte Carlo Success Probability

**Files:**

- Create: `Assetra.Core/Interfaces/Fire/IFireMonteCarloService.cs`
- Create: `Assetra.Application/Fire/FireMonteCarloService.cs`
- Create: `Assetra.Tests/Application/Fire/FireMonteCarloServiceTests.cs`
- Modify: `Assetra.Application/Fire/FirePlanningService.cs`
- Modify: `Assetra.WPF/Features/Fire/FireViewModel.cs`
- Modify: `Assetra.WPF/Features/Fire/FireView.xaml`

- [x] Add deterministic random seed support for tests.
- [x] Test: same seed produces same success rate.
- [x] Test: lower withdrawal and higher initial balance increases success probability.
- [x] Show success probability only in Advanced mode.
- [x] Add helper text:

```text
成功率是根據多次隨機報酬路徑估算，適合比較情境，不是保證結果。
```

- [x] Run:

```powershell
dotnet test Assetra.Tests\Assetra.Tests.csproj --filter FullyQualifiedName~Assetra.Tests.Application.Fire.FireMonteCarloServiceTests
```

Expected: Monte Carlo tests pass with stable seeded output.

- [ ] Commit:

```powershell
git add Assetra.Core\Interfaces\Fire Assetra.Application\Fire Assetra.Tests\Application\Fire Assetra.WPF\Features\Fire
git commit -m "feat: add FIRE success probability"
```

### Task 9: Integrate With Financial Goals

**Files:**

- Modify: `Assetra.WPF/Features/Fire/FireViewModel.cs`
- Modify: `Assetra.Tests/WPF/FireViewModelTests.cs`
- Modify if necessary: financial goal models/repositories.

- [x] Sync to Goals should save:
  - Goal name.
  - Required assets.
  - Current net worth.
  - Scenario id or scenario name in notes/metadata.
  - Target date when `YearsToFire` is known.
- [x] Test: syncing a scenario creates a FIRE goal with the required assets.
- [x] Test: syncing again updates existing FIRE goal instead of duplicating.
- [x] Test: deleting a scenario does not delete a financial goal silently.
- [x] Run:

```powershell
dotnet test Assetra.Tests\Assetra.Tests.csproj --filter FullyQualifiedName~Assetra.Tests.WPF.FireViewModelTests
```

Expected: goal sync tests pass.

- [ ] Commit:

```powershell
git add Assetra.WPF\Features\Fire Assetra.Tests\WPF\FireViewModelTests.cs
git commit -m "feat: sync FIRE scenarios to goals"
```

### Task 10: Add Guide And Release Notes

**Files:**

- Create: `docs/guides/FIRE-Planning.md`
- Modify: `docs/INDEX.md`
- Modify: `docs/releases/CHANGELOG.md`

- [x] Explain Basic mode.
- [x] Explain Advanced mode.
- [x] Explain `財務自由所需資產`.
- [x] Explain real vs nominal returns.
- [x] Explain drawdown and success probability.
- [x] Explain how FIRE syncs to financial goals.
- [x] Add release note entry.
- [x] Run:

```powershell
rg -n "FIRE 目標金額|FireNumber" docs Assetra.WPF Assetra.Application Assetra.Core
```

Expected: no stale user-facing `FIRE 目標金額` label remains except code identifiers, migration notes, or historical release notes.

- [ ] Commit:

```powershell
git add docs
git commit -m "docs: document FIRE planning"
```

---

## Acceptance Criteria

- [x] Basic mode returns the same result as the current calculator for the same inputs.
- [x] `AnnualExpenses = 600,000` and `WithdrawalRate = 0.04` displays `財務自由所需資產 = 15,000,000`.
- [x] `年支出` and `年儲蓄` show monthly helper values under the inputs.
- [x] The user can save at least one FIRE scenario and it survives app restart.
- [x] The user can mark one scenario as default.
- [x] App net worth can be used as the current net worth source.
- [x] Manual net worth override remains possible.
- [x] Advanced mode clearly separates real return from nominal return plus inflation.
- [x] Drawdown shows whether assets last until life expectancy.
- [x] Monte Carlo success probability is visible only when stochastic assumptions are available.
- [x] Sync to financial goals does not create duplicate FIRE goals.
- [x] Validation messages explain why the input is invalid.
- [x] The FIRE page has no WPF binding errors after loading and calculating.

---

## Verification Commands

Run the focused checks first:

```powershell
dotnet test Assetra.Tests\Assetra.Tests.csproj --filter FullyQualifiedName~Assetra.Tests.Application.Fire
dotnet test Assetra.Tests\Assetra.Tests.csproj --filter FullyQualifiedName~Assetra.Tests.WPF.FireViewModelTests
dotnet test Assetra.Tests\Assetra.Tests.csproj --filter FullyQualifiedName~FireScenarioSqliteRepositoryTests
```

Run the full project check before release:

```powershell
dotnet build Assetra.slnx
dotnet test Assetra.Tests\Assetra.Tests.csproj
```

Manual verification:

- Open FIRE page.
- Calculate in Basic mode.
- Save a scenario.
- Restart app.
- Confirm scenario reloads.
- Switch to Advanced mode.
- Add inflation and life expectancy.
- Confirm drawdown section appears.
- Sync to financial goals.
- Confirm the Goals page shows one FIRE goal with the same target.

---

## Risks And Guardrails

- Do not replace the current Basic calculator until its behavior is locked by tests.
- Do not mix nominal returns and real returns silently.
- Do not make Advanced mode the default first screen.
- Do not persist calculated projections as source of truth; persist scenario assumptions and recalculate projections.
- Do not let Monte Carlo block deterministic FIRE work.
- Do not overload financial goals with FIRE-specific assumptions unless a metadata shape already exists.
- Keep WPF view code-behind free of calculation logic.

---

## Recommended Delivery Order

1. Lock current Basic behavior with tests.
2. Add Core models and Application deterministic planning service.
3. Add drawdown service.
4. Add scenario repository.
5. Refactor WPF ViewModel around scenarios.
6. Rebuild FIRE UI into Basic / Advanced.
7. Sync scenarios to Goals.
8. Add Monte Carlo confidence.
9. Add docs and release notes.

This order keeps the current feature usable while gradually turning it into a complete FIRE planning module.
