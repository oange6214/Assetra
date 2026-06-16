# Add-Investment Flow Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reorganize the existing Buy transaction form into a Google-Finance-clean DEFAULT (數量 / 購買日期 / 單價 + an always-visible auto fee+tax+total summary) with everything advanced (價格模式、手續費覆寫、跨幣別 FX/結算) collapsed behind a 「進階」 `Expander` — keeping ALL existing functionality, just hidden until needed.

**Architecture:** Pure UI/layout reorganization of `BuyTxForm.xaml` + one small state addition to `BuyTxViewModel` (`IsAdvancedExpanded`, which auto-opens when a cross-currency trade is detected so the FX/settlement fields aren't hidden when they're required). The buy LOGIC — `ConfirmBuyAsync`, fee/tax/FX computation, the fee-preview summary, all validation — is **untouched**; only which fields are visible-by-default changes. The trade date (`TxDate`) already lives in the dialog shell and is shared by all tx types — it stays there.

**Tech Stack:** .NET 10 WPF, CommunityToolkit.Mvvm, xUnit. `TreatWarningsAsErrors` ON. Build `dotnet build Assetra.slnx`; test `dotnet test Assetra.Tests/Assetra.Tests.csproj`. This is one phase (~4 tasks); each is independently shippable.

---

## Current structure (verified — do not re-derive)

- **`Assetra.WPF/Features/Portfolio/Controls/TxForms/BuyTxForm.xaml`** — the buy form (bound to `TransactionDialogViewModel`; `Buy.*` resolves to the `BuyTxViewModel` sub-VM). Layout today (everything shown at once = the clutter):
  - **Stock/ETF** sub-form (`Buy.IsStock`, ~lines 29–374): trade-details header/hint → **Row1: 數量** (`AddAssetDialog.AddQuantity`) **| 價格模式 toggle 單價/總額** (`Buy.PriceMode`, two `SegmentBtn` RadioButtons) → price label + inline **取得市價** link (`AddAssetDialog.FetchMarketPriceCommand`) → **price input** (unit→`AddAssetDialog.AddPrice` / total→`Buy.TotalCost`, visibility-switched by `Buy.IsUnitMode`/`IsTotalMode`) → mirror line → **total-mode「金額已含手續費」CheckBox** (`Buy.TotalIncludesFee`) → ClosePrice hint → **Fee-preview Border** (成交金額/手續費/總成本/每股成本 — `AddGrossAmount`/`AddCommission`/`AddTotalCost`/`AddCostPerShare`, visible when `HasAddPreview`).
  - **Non-stock** (`Buy.IsNonStock`, ~377–396): Name + Cost.
  - **Crypto** (`Buy.IsCrypto`, ~399–429): Symbol + Qty + Price.
  - **Shared「Fee + cash-account」section** (~432–514, hidden in `Buy.MetaOnly`): **手續費 override** (`TxFee`, blank→auto-calc) + hint → **從現金帳戶扣款 CheckBox** (`TxUseCashAccount`) → cash-account `ComboBox` (`TxCashAccount`) → **`CrossCurrencyOverlay`** (the FX/settlement section, `DataContext={Binding Buy}`, `FetchBuyFxRateCommand`).
- **`Assetra.WPF/Features/Portfolio/SubViewModels/Tx/BuyTxViewModel.cs`** — owns the buy state. Already has `IsCrossCurrency` (computed: `InstrumentCurrency` vs settlement/cash currency), `IsUnitMode`/`IsTotalMode`, `PriceMode`, `TotalIncludesFee`, and a `Reset()`. The `IsCrossCurrency` value changes from `OnInstrumentCurrencyChanged`, `OnCashAccountCurrencyChanged`, `OnSettlementCurrencyChanged` (all raise `nameof(IsCrossCurrency)`).
- **Trade date:** `AddRecordDialog.xaml:411-415` — `<DatePicker SelectedDate="{Binding TxDate, ...}" Style="{StaticResource AppDatePicker}"/>` in the dialog SHELL, shared by all tx types. **Leave it where it is** (do not move shared infra into the buy form). The design's "購買日期 in default" requirement is already met by the shell; only verify (Task 2) that it sits visually near the economic fields — if it's far below, a small shell reposition is in scope, otherwise leave it.
- **`Expander` pattern:** use `Style="{StaticResource AppExpander}"` (as in `Reports/ReportsView.xaml`, `Categories`, `MonteCarlo`) with `Header` + `IsExpanded`. No numeric stepper control exists in the codebase.

---

## DEFAULT vs 進階 split (the design)

**DEFAULT (always visible):**
- Stock: 數量, the **單位價格** input (unit mode) + 取得市價 link, the **Fee-preview Border** (the read-only 手續費/總成本/每股成本 summary — this IS the "see the tax/fees without entering them" piece).
- Non-stock: Name + Cost. Crypto: Symbol + Qty + Price.
- (Trade date stays in the shell.)

**進階 (collapsed `Expander`, header「進階」):**
- Stock-only: **價格模式 toggle (單價/總額)** + the total-mode input + 「金額已含手續費」.
- Shared (all buy types): **手續費 override** (+ hint), **從現金帳戶扣款** + cash-account combo, **`CrossCurrencyOverlay`** (FX/settlement).

**Smart auto-expand:** when `Buy.IsCrossCurrency` becomes true (instrument currency ≠ cash-account currency → FX/settlement is required), the 進階 expander auto-opens. It does NOT auto-collapse on same-currency (don't fight a user who opened it).

---

## Task 1: `BuyTxViewModel.IsAdvancedExpanded` + cross-currency auto-expand

**Files:** Modify `Assetra.WPF/Features/Portfolio/SubViewModels/Tx/BuyTxViewModel.cs`; Test `Assetra.Tests/WPF/BuyTxViewModelTests.cs` (create if absent — grep first).

- [ ] **Step 1: Write the failing tests.**

```csharp
using Assetra.WPF.Features.Portfolio.SubViewModels.Tx;
using Xunit;

namespace Assetra.Tests.WPF;

public class BuyTxViewModelAdvancedExpansionTests
{
    [Fact]
    public void IsAdvancedExpanded_DefaultsFalse()
    {
        var vm = new BuyTxViewModel();
        Assert.False(vm.IsAdvancedExpanded);
    }

    [Fact]
    public void CrossCurrencyTrade_AutoExpandsAdvanced()
    {
        var vm = new BuyTxViewModel();
        vm.InstrumentCurrency = "USD";
        vm.CashAccountCurrency = "TWD";          // USD ≠ TWD → cross-currency
        Assert.True(vm.IsCrossCurrency);
        Assert.True(vm.IsAdvancedExpanded);       // auto-opened so FX/settlement isn't hidden
    }

    [Fact]
    public void SameCurrency_DoesNotForceCollapse_AfterUserExpanded()
    {
        var vm = new BuyTxViewModel { IsAdvancedExpanded = true };  // user opened it
        vm.InstrumentCurrency = "TWD";
        vm.CashAccountCurrency = "TWD";          // same currency
        Assert.False(vm.IsCrossCurrency);
        Assert.True(vm.IsAdvancedExpanded);       // NOT force-collapsed — one-way auto-expand only
    }

    [Fact]
    public void Reset_CollapsesAdvanced()
    {
        var vm = new BuyTxViewModel { IsAdvancedExpanded = true };
        vm.Reset();
        Assert.False(vm.IsAdvancedExpanded);
    }
}
```

- [ ] **Step 2: Run — Expected FAIL** (no `IsAdvancedExpanded`). `dotnet test Assetra.Tests/Assetra.Tests.csproj --filter FullyQualifiedName~BuyTxViewModelAdvancedExpansion`

- [ ] **Step 3: Implement.** Add the property + the one-way auto-expand, and collapse on Reset:

```csharp
/// <summary>True 時「進階」區塊展開。跨幣別交易會自動設為 true（FX/結算為必填），
/// 但同幣別不會強制收合，避免跟手動展開的使用者打架。</summary>
[ObservableProperty] private bool _isAdvancedExpanded;

private void AutoExpandAdvancedIfCrossCurrency()
{
    if (IsCrossCurrency)
        IsAdvancedExpanded = true;   // one-way: open when needed, never force-close
}
```
Call `AutoExpandAdvancedIfCrossCurrency()` from the END of each handler that already recomputes `IsCrossCurrency` — `OnInstrumentCurrencyChanged`, `OnCashAccountCurrencyChanged`, `OnSettlementCurrencyChanged` (add the call after their existing `OnPropertyChanged(nameof(IsCrossCurrency))`). In `Reset()`, add `IsAdvancedExpanded = false;` (near the other resets, before/after the P3 block — order doesn't matter).

- [ ] **Step 4: Run — Expected PASS.**
- [ ] **Step 5: Commit.** `git commit -m "feat(portfolio): BuyTxViewModel.IsAdvancedExpanded + 跨幣別自動展開進階"`

## Task 2: Reorganize `BuyTxForm.xaml` — clean default + 進階 Expander

**Files:** Modify `Assetra.WPF/Features/Portfolio/Controls/TxForms/BuyTxForm.xaml`

This is a layout move — **every existing binding must be preserved verbatim**; do not rename or rebind anything, only relocate elements and wrap the advanced ones in an `Expander`.

- [ ] **Step 1: Default to unit price mode on open.** Confirm `BuyTxViewModel.Reset()` sets `PriceMode = "unit"` (it does, line ~242) — so the default-visible price input is the unit-price `TextBox`. No change needed; just verify.

- [ ] **Step 2: Wrap the STOCK-only advanced fields in the Expander.** In the stock sub-form, the 進階 collapsible content = the **價格模式 toggle Grid** (the `Grid.Column="2"` StackPanel containing the two `SegmentBtn` RadioButtons, ~lines 92–125), the **total-mode price input** (`Buy.TotalCost` TextBox, ~210–215) , and the **「金額已含手續費」CheckBox** (~248–254). Keep 數量 (the `Grid.Column="0"` quantity), the **unit-price input** (`AddPrice`, ~204–209), the 取得市價 link, the mirror lines, and the **Fee-preview Border** (~285–372) in the DEFAULT (outside the Expander). Because the price-mode toggle moves into 進階, the unit-price input stays the visible default; opening 進階 and switching to 總額 swaps to the total input (both already visibility-switch on `Buy.IsUnitMode`/`IsTotalMode`).

- [ ] **Step 3: Wrap the SHARED advanced section in the same Expander.** The「Fee + cash-account」`StackPanel` (~432–514: 手續費 override, 從現金帳戶扣款 + combo, `CrossCurrencyOverlay`) moves inside the 進階 Expander content too. Net: there is ONE `Expander` near the bottom of the buy form holding both the stock-only advanced bits and the shared advanced section. Structure it as:

```xml
<Expander
    Margin="0,8,0,0"
    Style="{StaticResource AppExpander}"
    Header="{DynamicResource Portfolio.Tx.AdvancedSection}"
    IsExpanded="{Binding Buy.IsAdvancedExpanded, Mode=TwoWay}">
    <StackPanel>
        <!-- moved: 價格模式 toggle + 總額 input + 金額已含手續費 (stock-only — keep their
             existing Buy.IsStock / IsTotalMode visibility triggers so they stay hidden for
             non-stock/crypto) -->
        <!-- moved: 手續費 override + hint -->
        <!-- moved: 從現金帳戶扣款 + cash combo + CrossCurrencyOverlay (the whole MetaOnly-aware
             StackPanel body, minus its outer MetaOnly visibility wrapper which must be preserved) -->
    </StackPanel>
</Expander>
```
⚠️ Preserve the `Buy.MetaOnly`-driven visibility on the shared section, and the per-element visibility triggers (`Buy.IsStock`, `IsUnitMode`/`IsTotalMode`, `TxUseCashAccount`). The `CrossCurrencyOverlay`'s `DataContext="{Binding Buy}"` + `FetchFxRateCommand="{Binding DataContext.FetchBuyFxRateCommand, ElementName=Root}"` must stay exactly as-is (the `ElementName=Root` reference still resolves — `Root` is the UserControl). Do NOT change `TxFee`, `AddPrice`, `AddQuantity`, `TotalCost`, `TxCashAccount`, or any other binding path.

- [ ] **Step 4: Build.** `dotnet build Assetra.slnx -v minimal` → **0/0**. (A missing `StaticResource AppExpander` or a broken binding would surface — but note a missing `DynamicResource` lang key is RUNTIME, see Task 3; and bindings are runtime too, so the user must visually verify in Task 4.)

- [ ] **Step 5: Commit.** `git commit -m "feat(portfolio): 買入表單預設精簡 + 進階區塊收合（功能不變）"`

## Task 3: Localization for the 進階 header

**Files:** Modify `Assetra.WPF/Languages/zh-TW.xaml`, `en-US.xaml`

- [ ] **Step 1: Add the key to BOTH files** (a missing key in one renders blank in that language with no build error):
  - zh-TW: `<sys:String x:Key="Portfolio.Tx.AdvancedSection">進階（手續費 / 跨幣別 / 結算）</sys:String>`
  - en-US: `<sys:String x:Key="Portfolio.Tx.AdvancedSection">Advanced (fees / FX / settlement)</sys:String>`
  Place near the other `Portfolio.Tx.*` keys. grep to confirm the key isn't already defined.
- [ ] **Step 2: Build → 0/0.**
- [ ] **Step 3: Commit.** `git commit -m "i18n(portfolio): 進階區塊標題 key（雙語）"`

## Task 4: Verify (build + full suite + visual)

- [ ] **Step 1:** `dotnet build Assetra.slnx` → 0/0; `dotnet test Assetra.Tests/Assetra.Tests.csproj` → all green (baseline + the 4 new BuyTxViewModel tests). Report counts.
- [ ] **Step 2:** ⚠️ **Runtime/visual verification is the user's** (relaunch): open ＋投資 → buy a TW stock → confirm the DEFAULT shows only 數量 / 單價 / fee-preview (+ shell date), with 「進階」collapsed; expand it → 價格模式 / 手續費 override / cash-account / FX all present and working; select a USD instrument with a TWD cash account → confirm 進階 **auto-expands**. Test in both light & dark themes. (Bindings + DynamicResource keys are runtime-resolved — build green does NOT prove the form renders; this step is mandatory.)

---

## Out of scope (confirmed)
- Multi-lot inline entry (user chose option A — defer). The per-lot purchase history is already covered by the existing side panel.
- Any change to buy/sell LOGIC, fee/tax/FX computation, or validation. Sell/Dividend forms (which also use `CrossCurrencyOverlay`) are NOT touched.
- Moving the shared `TxDate` out of the dialog shell.

## Self-review
- Spec coverage: clean default (Task 2 default cluster + existing fee-preview), 進階 collapse (Task 2 Expander), auto fee/tax visible (existing Fee-preview kept in default), smart auto-expand on cross-currency (Task 1), 購買日期 in default (shell `TxDate`, verified Task 2 Step + Task 4). ✔
- No-logic-change: only `IsAdvancedExpanded` (new state) added; all bindings relocated, none rebound. ✔
- Runtime-risk called out: Task 4 mandates visual verify because bindings + `DynamicResource` keys can't be build-checked (the P1 `StaticResource` regression lesson). ✔
- Only genuinely-new code = `IsAdvancedExpanded` + the one-way auto-expand (full code + 4 tests in Task 1); the rest is element relocation with verbatim-preserved bindings. ✔
