# Cross-Currency Transaction UX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the foreign-trade entry form explain the difference between trade currency, price input mode, cash settlement, and FX without making users feel those controls duplicate each other.

**Architecture:** This is a UX copy and XAML layout convergence pass. Keep the existing transaction domain model, persistence fields, FX resolver, validation, and cash-account deduction behavior unchanged; only clarify labels, helper text, section grouping, and badge severity. Buy, sell, and dividend forms must use the same settlement vocabulary so users learn one model.

**Tech Stack:** WPF XAML, Assetra DesignSystem resources, DynamicResource language dictionaries, xUnit text-based XAML regression tests, .NET Debug build.

---

## Product Decisions

Use these terms consistently:

- `成交幣別` means the instrument/trade currency. It controls unit price, total trade amount, and default fee currency.
- `成交金額輸入方式` means how the user enters the trade amount: `每股價格` or `成交總額`.
- `扣款與匯率` means the cash movement that hits the funding account when the trade currency and account currency differ.
- `實際扣款金額（依券商對帳單）` is the most accurate cash amount. For sub-brokerage, it may already include commission, tax, broker FX spread, and settlement differences.
- `匯率` remains supporting metadata and an estimator. It is not more authoritative than the broker statement.

Do not add new fields in this pass. Do not change confirmation math unless a regression test proves the current persisted values are wrong.

## File Map

- Modify: `D:/Workspaces/Finances/Assetra/Assetra.WPF/Languages/zh-TW.xaml`
  - Chinese labels and helper text for trade amount, settlement, FX, and required/recommended badges.
- Modify: `D:/Workspaces/Finances/Assetra/Assetra.WPF/Languages/en-US.xaml`
  - English equivalents so language switching remains complete.
- Modify: `D:/Workspaces/Finances/Assetra/Assetra.WPF/Features/Portfolio/Controls/TxForms/BuyTxForm.xaml`
  - Rename the section, demote the badge from danger styling, and align the settlement section hierarchy.
- Modify: `D:/Workspaces/Finances/Assetra/Assetra.WPF/Features/Portfolio/Controls/TxForms/SellTxForm.xaml`
  - Replace old banner + advanced wording with the same cash-settlement language used by buy.
- Modify: `D:/Workspaces/Finances/Assetra/Assetra.WPF/Features/Portfolio/Controls/TxForms/CashDividendTxForm.xaml`
  - Same settlement language for dividend credit flow.
- Modify: `D:/Workspaces/Finances/Assetra/Assetra.Tests/WPF/ControlsBehaviorTests.cs`
  - Add narrow XAML/resource regression checks so the UI does not drift back to duplicate or alarming wording.
- Modify: `D:/Workspaces/Finances/Assetra/docs/architecture/Transaction-FX-Settlement.md`
  - Document the user-facing terminology and authority order.

---

### Task 1: Lock The Vocabulary With Regression Tests

**Files:**
- Modify: `D:/Workspaces/Finances/Assetra/Assetra.Tests/WPF/ControlsBehaviorTests.cs`

- [x] **Step 1: Add helper path lookup for language dictionaries if it does not already exist**

Add this helper near `GetBuyTxFormPath()`:

```csharp
private static string GetLanguagePath(string fileName)
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        var candidate = Path.Combine(
            current.FullName,
            "Assetra.WPF",
            "Languages",
            fileName);
        if (File.Exists(candidate))
            return candidate;

        current = current.Parent;
    }

    throw new FileNotFoundException($"Could not locate language dictionary {fileName}.");
}
```

- [x] **Step 2: Add a failing vocabulary test**

Add this test:

```csharp
[Fact]
public void TransactionFxCopy_UsesCashSettlementVocabulary()
{
    var zh = File.ReadAllText(GetLanguagePath("zh-TW.xaml"));
    var en = File.ReadAllText(GetLanguagePath("en-US.xaml"));

    Assert.Contains("成交金額輸入方式", zh);
    Assert.Contains("每股價格", zh);
    Assert.Contains("成交總額", zh);
    Assert.Contains("扣款與匯率", zh);
    Assert.Contains("實際扣款金額（依券商對帳單）", zh);
    Assert.Contains("對帳單金額", zh);
    Assert.DoesNotContain(">結算與匯率<", zh);
    Assert.DoesNotContain(">複委託請填<", zh);

    Assert.Contains("Trade amount input", en);
    Assert.Contains("Per-share price", en);
    Assert.Contains("Trade total", en);
    Assert.Contains("Cash settlement and FX", en);
    Assert.Contains("Actual cash debit (from broker statement)", en);
    Assert.DoesNotContain(">Settlement and FX<", en);
    Assert.DoesNotContain(">Required for sub-brokerage<", en);
}
```

- [x] **Step 3: Run the focused test and verify it fails**

Run:

```powershell
dotnet test D:\Workspaces\Finances\Assetra\Assetra.Tests\Assetra.Tests.csproj -c Debug --no-restore --filter "ControlsBehaviorTests.TransactionFxCopy_UsesCashSettlementVocabulary" --logger "console;verbosity=minimal"
```

Expected: FAIL because the current resources still contain `價格輸入模式`, `結算與匯率`, and `複委託請填`.

---

### Task 2: Rename Resource Strings

**Files:**
- Modify: `D:/Workspaces/Finances/Assetra/Assetra.WPF/Languages/zh-TW.xaml`
- Modify: `D:/Workspaces/Finances/Assetra/Assetra.WPF/Languages/en-US.xaml`

- [x] **Step 1: Update Chinese resources**

Replace these resource values:

```xml
<sys:String x:Key="Portfolio.Tx.PriceMode">成交金額輸入方式</sys:String>
<sys:String x:Key="Portfolio.Tx.PriceModeUnit">每股價格</sys:String>
<sys:String x:Key="Portfolio.Tx.PriceModeTotal">成交總額</sys:String>
<sys:String x:Key="Portfolio.Tx.ActualCashAmount">實際扣款金額（依券商對帳單）</sys:String>
<sys:String x:Key="Portfolio.Tx.ActualCashAmountHint">若券商對帳單已有實際扣款，請直接填此欄；複委託通常已包含手續費、交易稅與換匯價差。留空時系統會用成交價、股數、手續費與匯率估算。</sys:String>
<sys:String x:Key="Portfolio.Tx.SettlementSection">扣款與匯率</sys:String>
<sys:String x:Key="Portfolio.Tx.FetchFxRate">取得匯率</sys:String>
<sys:String x:Key="Portfolio.Tx.CrossCurrency.Hint.Short">成交幣別與扣款幣別不同時，請優先填券商對帳單上的實際扣款金額；沒有對帳單時再用匯率估算。</sys:String>
<sys:String x:Key="Portfolio.Tx.ActualCashAmount.RequiredBadge">對帳單金額</sys:String>
<sys:String x:Key="Portfolio.Tx.FxRate">匯率（成交幣別 → 扣款幣別）</sys:String>
<sys:String x:Key="Portfolio.Tx.FxRate.Hint">例：美股以 TWD 帳戶扣款時，請填 USD → TWD 匯率。若已填實際扣款金額，匯率只作為估算與紀錄參考。</sys:String>
<sys:String x:Key="Portfolio.Tx.SellActualCashAmount">實際入帳金額（依券商對帳單）</sys:String>
<sys:String x:Key="Portfolio.Tx.SellActualCashAmountHint">若券商對帳單已有實際入帳，請直接填此欄；複委託通常已包含手續費、交易稅與換匯價差。留空時系統會用賣出價、股數、手續費與匯率估算。</sys:String>
<sys:String x:Key="Portfolio.Tx.DivActualCashAmount">實際入帳金額（依券商對帳單）</sys:String>
<sys:String x:Key="Portfolio.Tx.DivActualCashAmountHint">若券商對帳單已有實際入帳，請直接填此欄；留空時系統會用每股股利、持股數與匯率估算。</sys:String>
```

- [x] **Step 2: Update English resources**

Replace these resource values:

```xml
<sys:String x:Key="Portfolio.Tx.PriceMode">Trade amount input</sys:String>
<sys:String x:Key="Portfolio.Tx.PriceModeUnit">Per-share price</sys:String>
<sys:String x:Key="Portfolio.Tx.PriceModeTotal">Trade total</sys:String>
<sys:String x:Key="Portfolio.Tx.ActualCashAmount">Actual cash debit (from broker statement)</sys:String>
<sys:String x:Key="Portfolio.Tx.ActualCashAmountHint">If the broker statement shows the actual debit, enter it here. Sub-brokerage amounts often include commission, tax, and FX spread. If left blank, Assetra estimates from price, shares, fee, and FX rate.</sys:String>
<sys:String x:Key="Portfolio.Tx.SettlementSection">Cash settlement and FX</sys:String>
<sys:String x:Key="Portfolio.Tx.FetchFxRate">Get FX rate</sys:String>
<sys:String x:Key="Portfolio.Tx.CrossCurrency.Hint.Short">When trade currency and debit currency differ, prefer the broker statement cash amount. Use the FX rate only when the statement amount is not available.</sys:String>
<sys:String x:Key="Portfolio.Tx.ActualCashAmount.RequiredBadge">Statement amount</sys:String>
<sys:String x:Key="Portfolio.Tx.FxRate">FX rate (trade currency → debit currency)</sys:String>
<sys:String x:Key="Portfolio.Tx.FxRate.Hint">Example: US stock paid from a TWD account uses USD → TWD. If the actual cash debit is entered, FX is supporting estimate metadata.</sys:String>
<sys:String x:Key="Portfolio.Tx.SellActualCashAmount">Actual cash credit (from broker statement)</sys:String>
<sys:String x:Key="Portfolio.Tx.SellActualCashAmountHint">If the broker statement shows the actual credit, enter it here. Sub-brokerage amounts often include commission, tax, and FX spread. If left blank, Assetra estimates from sell price, shares, fee, and FX rate.</sys:String>
<sys:String x:Key="Portfolio.Tx.DivActualCashAmount">Actual cash credit (from broker statement)</sys:String>
<sys:String x:Key="Portfolio.Tx.DivActualCashAmountHint">If the broker statement shows the actual credit, enter it here. If left blank, Assetra estimates from dividend per share, shares, and FX rate.</sys:String>
```

- [x] **Step 3: Run the vocabulary test again**

Run:

```powershell
dotnet test D:\Workspaces\Finances\Assetra\Assetra.Tests\Assetra.Tests.csproj -c Debug --no-restore --filter "ControlsBehaviorTests.TransactionFxCopy_UsesCashSettlementVocabulary" --logger "console;verbosity=minimal"
```

Expected: PASS.

---

### Task 3: Demote The Buy Settlement Badge From Error To Information

**Files:**
- Modify: `D:/Workspaces/Finances/Assetra/Assetra.WPF/Features/Portfolio/Controls/TxForms/BuyTxForm.xaml`
- Modify: `D:/Workspaces/Finances/Assetra/Assetra.Tests/WPF/ControlsBehaviorTests.cs`

- [x] **Step 1: Add a failing XAML regression test**

Add this test:

```csharp
[Fact]
public void BuyTxForm_CashSettlementBadgeIsInformationalNotDanger()
{
    var xaml = File.ReadAllText(GetBuyTxFormPath());

    Assert.Contains("Text=\"{DynamicResource Portfolio.Tx.ActualCashAmount.RequiredBadge}\"", xaml);
    Assert.DoesNotContain("Background=\"{DynamicResource AppDangerSubtle}\"", xaml);
    Assert.DoesNotContain("Foreground=\"{DynamicResource AppDanger}\"", xaml);
    Assert.Contains("Style=\"{StaticResource StatusBadge.Info}\"", xaml);
}
```

- [x] **Step 2: Run the test and verify it fails**

Run:

```powershell
dotnet test D:\Workspaces\Finances\Assetra\Assetra.Tests\Assetra.Tests.csproj -c Debug --no-restore --filter "ControlsBehaviorTests.BuyTxForm_CashSettlementBadgeIsInformationalNotDanger" --logger "console;verbosity=minimal"
```

Expected: FAIL because the buy form currently uses danger badge brushes.

- [x] **Step 3: Replace the hand-built danger badge with `StatusBadge.Info`**

In `BuyTxForm.xaml`, replace the badge `Border` inside the actual cash amount label with:

```xml
<Border
    Margin="8,0,0,0"
    DockPanel.Dock="Left"
    VerticalAlignment="Center"
    Style="{StaticResource StatusBadge.Info}">
    <TextBlock
        Style="{StaticResource StatusBadge.Text}"
        Text="{DynamicResource Portfolio.Tx.ActualCashAmount.RequiredBadge}" />
</Border>
```

- [x] **Step 4: Run the focused test**

Run:

```powershell
dotnet test D:\Workspaces\Finances\Assetra\Assetra.Tests\Assetra.Tests.csproj -c Debug --no-restore --filter "ControlsBehaviorTests.BuyTxForm_CashSettlementBadgeIsInformationalNotDanger" --logger "console;verbosity=minimal"
```

Expected: PASS.

---

### Task 4: Align Sell And Dividend Settlement UI Language

**Files:**
- Modify: `D:/Workspaces/Finances/Assetra/Assetra.WPF/Features/Portfolio/Controls/TxForms/SellTxForm.xaml`
- Modify: `D:/Workspaces/Finances/Assetra/Assetra.WPF/Features/Portfolio/Controls/TxForms/CashDividendTxForm.xaml`
- Modify: `D:/Workspaces/Finances/Assetra/Assetra.Tests/WPF/ControlsBehaviorTests.cs`

- [x] **Step 1: Add helper path lookups**

Add:

```csharp
private static string GetTxFormPath(string fileName)
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        var candidate = Path.Combine(
            current.FullName,
            "Assetra.WPF",
            "Features",
            "Portfolio",
            "Controls",
            "TxForms",
            fileName);
        if (File.Exists(candidate))
            return candidate;

        current = current.Parent;
    }

    throw new FileNotFoundException($"Could not locate transaction form {fileName}.");
}
```

Then simplify `GetBuyTxFormPath()` to:

```csharp
private static string GetBuyTxFormPath() => GetTxFormPath("BuyTxForm.xaml");
```

- [x] **Step 2: Add a failing consistency test**

Add:

```csharp
[Fact]
public void CrossCurrencyTxForms_UseSharedSettlementBadgeStyle()
{
    var sell = File.ReadAllText(GetTxFormPath("SellTxForm.xaml"));
    var dividend = File.ReadAllText(GetTxFormPath("CashDividendTxForm.xaml"));

    Assert.Contains("Style=\"{StaticResource StatusBadge.Info}\"", sell);
    Assert.Contains("Style=\"{StaticResource StatusBadge.Info}\"", dividend);
    Assert.DoesNotContain("Background=\"{DynamicResource AppDangerSubtle}\"", sell);
    Assert.DoesNotContain("Foreground=\"{DynamicResource AppDanger}\"", sell);
    Assert.DoesNotContain("Background=\"{DynamicResource AppDangerSubtle}\"", dividend);
    Assert.DoesNotContain("Foreground=\"{DynamicResource AppDanger}\"", dividend);
}
```

- [x] **Step 3: Replace sell badge styling**

In `SellTxForm.xaml`, replace the `AppDangerSubtle` badge border with:

```xml
<Border
    Margin="8,0,0,0"
    DockPanel.Dock="Left"
    VerticalAlignment="Center"
    Style="{StaticResource StatusBadge.Info}"
    Visibility="{Binding Sell.IsCrossCurrency, Converter={StaticResource BooleanToVisibilityConverter}}">
    <TextBlock
        Style="{StaticResource StatusBadge.Text}"
        Text="{DynamicResource Portfolio.Tx.ActualCashAmount.RequiredBadge}" />
</Border>
```

- [x] **Step 4: Replace dividend badge styling**

In `CashDividendTxForm.xaml`, replace the `AppDangerSubtle` badge border with:

```xml
<Border
    Margin="8,0,0,0"
    DockPanel.Dock="Left"
    VerticalAlignment="Center"
    Style="{StaticResource StatusBadge.Info}"
    Visibility="{Binding Div.IsCrossCurrency, Converter={StaticResource BooleanToVisibilityConverter}}">
    <TextBlock
        Style="{StaticResource StatusBadge.Text}"
        Text="{DynamicResource Portfolio.Tx.ActualCashAmount.RequiredBadge}" />
</Border>
```

- [x] **Step 5: Run the consistency test**

Run:

```powershell
dotnet test D:\Workspaces\Finances\Assetra\Assetra.Tests\Assetra.Tests.csproj -c Debug --no-restore --filter "ControlsBehaviorTests.CrossCurrencyTxForms_UseSharedSettlementBadgeStyle" --logger "console;verbosity=minimal"
```

Expected: PASS.

---

### Task 5: Document Authority Order

**Files:**
- Modify: `D:/Workspaces/Finances/Assetra/docs/architecture/Transaction-FX-Settlement.md`

- [x] **Step 1: Add a terminology section**

Add this after the opening summary:

```markdown
## User-Facing Terminology

- **Trade currency / 成交幣別**: the currency of the instrument price and trade amount.
- **Trade amount input / 成交金額輸入方式**: whether the user enters per-share price or total trade amount.
- **Cash settlement / 扣款與匯率**: the cash-account movement when the account currency differs from the trade currency.
- **Actual cash debit or credit / 實際扣款或入帳金額（依券商對帳單）**: the broker statement amount and the preferred source of truth.

Authority order:

1. Broker statement actual cash amount, when present.
2. Explicit FX rate entered or fetched for the trade date.
3. Estimated trade amount from price, quantity, and fee.

FX is metadata and estimation support. It should not override an actual broker statement amount.
```

- [x] **Step 2: Run a docs text check**

Run:

```powershell
rg -n "User-Facing Terminology|Authority order|Broker statement actual cash amount" D:\Workspaces\Finances\Assetra\docs\architecture\Transaction-FX-Settlement.md
```

Expected: all three phrases are found.

---

### Task 6: Build And Verify

**Files:**
- Verify only.

- [x] **Step 1: Run focused WPF UI regression tests**

Run:

```powershell
dotnet test D:\Workspaces\Finances\Assetra\Assetra.Tests\Assetra.Tests.csproj -c Debug --no-restore --filter "ControlsBehaviorTests" --logger "console;verbosity=minimal"
```

Expected: PASS. If the app or Visual Studio locks `bin/Debug`, rerun with:

```powershell
dotnet test D:\Workspaces\Finances\Assetra\Assetra.Tests\Assetra.Tests.csproj -c Debug --no-restore --filter "ControlsBehaviorTests" --logger "console;verbosity=minimal" -p:OutDir=D:\Workspaces\Finances\Assetra\.verify\test\
```

- [x] **Step 2: Build WPF**

Run:

```powershell
dotnet build D:\Workspaces\Finances\Assetra\Assetra.WPF\Assetra.WPF.csproj -c Debug --no-restore
```

Expected: build succeeds. If output files are locked, rerun with:

```powershell
dotnet build D:\Workspaces\Finances\Assetra\Assetra.WPF\Assetra.WPF.csproj -c Debug --no-restore -p:OutDir=D:\Workspaces\Finances\Assetra\.verify\wpf\
```

- [x] **Step 3: Clean alternate verification output if used**

Run only if `.verify` was created:

```powershell
Remove-Item -LiteralPath D:\Workspaces\Finances\Assetra\.verify -Recurse -Force
```

- [ ] **Step 4: Manual smoke check**

Open the app and verify:

- Buy USD stock with TWD cash account shows `扣款與匯率`.
- The top currency field reads as a condition for the trade, not as a duplicate of settlement.
- Price mode reads `成交金額輸入方式 / 每股價格 / 成交總額`.
- Actual cash amount label explains broker statement authority.
- Badge is informational, not red danger.
- Same wording pattern appears in sell and cash dividend cross-currency flows.

---

## Self-Review

- Spec coverage: This plan covers the user's concern about overlapping meaning between currency, price mode, total amount, actual cash amount, sub-brokerage settlement, and FX.
- Scope control: No persistence, repository, domain model, or transaction math changes are included.
- Consistency: Buy, sell, and dividend flows are included because all three expose cross-currency cash settlement.
- Verification: Includes failing tests first, focused tests after implementation, WPF build, docs check, and manual smoke criteria.
