# H1 — TransactionDialog God-Object Split

**Status:** Planning  
**Estimated effort:** 16–32h  
**Priority:** High (blocks per-transaction-type unit testing & maintainability)

---

## Problem

`Assetra.WPF/Features/Portfolio/SubViewModels/TransactionDialogViewModel.cs` (1,254 lines) +
`.Confirm.cs` (643) + `.Categories.cs` (128) = **2,025 lines, 60+ ObservableProperties, 5+ RelayCommands** in a single partial class.

It owns the form state for **9 distinct transaction types** plus shared meta:

1. Buy (with unit/total mode, ETF / bond-ETF flags, commission)
2. Sell (gross/commission/tax/net cascade, sell quantity)
3. Cash dividend (per-share vs total mode, position picker)
4. Stock dividend (new-shares input)
5. Income / Deposit (cash account, label)
6. Withdrawal (cash account, fee)
7. Transfer (source + target accounts, FX rate, target amount)
8. Loan (borrow / repay / charge), with rate / term / start-date sub-form
9. Credit card (charge / payment)

Plus revision-mode plumbing, edit-summary plumbing, suggestion lists for cash account / position, and per-field error strings.

**Symptoms:**

- 60 properties → impossible to reason about which apply to which TxType
- Bug surface across types (changing one field path can break others)
- `ConfirmTxAsync` in `.Confirm.cs` is one giant switch with shared validation and per-type branches
- Tests use a fixture that requires constructing the entire VM even for type-isolated assertions

---

## Target Design

```
TransactionDialogViewModel              ← shell / orchestrator (≤ 250 lines)
├── ITxTypeViewModel.IsActive           ← discriminator
├── ITxTypeViewModel.Validate()         ← per-type validation
├── ITxTypeViewModel.BuildTrade(TxContext) → IReadOnlyList<Trade>
└── child VMs (created via factory, switched on TxType):
    ├── BuyTxViewModel
    ├── SellTxViewModel
    ├── CashDividendTxViewModel
    ├── StockDividendTxViewModel
    ├── DepositWithdrawalTxViewModel  (income / deposit / withdrawal share enough state)
    ├── TransferTxViewModel
    ├── LoanTxViewModel
    └── CreditCardTxViewModel
```

**Shared concerns (stay in shell):**

- `IsTxDialogOpen`, `TxType`, `EditingTradeId`, revision-mode flags
- `TxDate`, `TxNote`, `TxError`
- Cash-account / position autocomplete suggestions
- Edit-summary block (read-only)
- `OpenAddTx`, `CloseTxDialog`, `ConfirmTx` commands

**Per-type concerns (move to child VMs):**

- All `Tx{Type}*` properties and their error counterparts
- Field-level validation (`Tx{Type}Error` setters, async lookups for autocomplete)
- `BuildTrade()` — currently inline inside `ConfirmTxAsync` switch

---

## Migration Plan

### Phase 1 — Extract per-type VMs (no behaviour change)

Each phase is one PR / commit; tree compiles green between phases.

1. **Create skeleton:** `ITxTypeViewModel` + 8 stub partial VMs in `SubViewModels/Tx/` folder.
2. **Buy** — move `TxBuy*` properties + `OnTxBuyTotalCostChanged` + buy branch of `ConfirmTxAsync` into `BuyTxViewModel`. Shell delegates: `IsTxDialogOpen && TxType == "buy"` → `BuyTxVm.Validate(); var trades = BuyTxVm.BuildTrade(ctx)`.
3. **Sell** — same pattern. Sell has FIFO lot lookup; pass `ITradeRepository` via TxContext.
4. **CashDividend** — includes per-share/total mode toggle.
5. **StockDividend** — small; combine with CashDividend file if helpful.
6. **DepositWithdrawal** — share `Income` / `Deposit` / `Withdrawal`. Discriminate on shell `TxType`.
7. **Transfer** — owns target-account + FX-rate state.
8. **Loan** — owns rate/term/start-date sub-form; loan-borrow / loan-repay branches.
9. **CreditCard** — charge + payment.

### Phase 2 — Slim shell

After all 8 are extracted:

- Delete now-empty members from shell.
- Replace big `ConfirmTxAsync` switch with `ActiveTypeVm.BuildTrade(ctx)`.
- Validation entry point becomes `ActiveTypeVm.Validate(out List<TxFieldError>)`.

### Phase 3 — Tests

- Move `PortfolioViewModelTests` per-type cases into per-type test classes (e.g., `BuyTxViewModelTests`).
- Each per-type VM is unit-testable with just its own deps + a `TxContext` factory; Portfolio shell tests stop multiplying.

---

## Risks & Mitigations

| Risk | Mitigation |
|---|---|
| XAML bindings break (60+ properties referenced) | Phase the rewrite; keep shell properties as thin pass-throughs (`public string TxBuyTotalCost => Buy.TxBuyTotalCost`) until all XAML files are repointed. Then drop the pass-throughs. |
| Cross-type reads (e.g., `Buy` reading `TxNote`) | Cross-type reads always go through shell context (`TxContext`). No child VM reads another's properties. |
| Revision mode touches multiple types | Shell owns `IsRevisionMode`; child VMs accept it via `Activate(TxContext)`. |
| Test-only positional ctor breaks | After split, the shell ctor signature stays stable — child VMs have separate ctors. |

---

## Acceptance Criteria

1. `TransactionDialogViewModel.cs` shell ≤ 250 lines
2. Each child VM ≤ 250 lines
3. No child VM references another child VM
4. Existing `PortfolioViewModelTests` all pass without modification
5. New per-type test classes added with at least the validation paths
6. XAML compiles (no removed binding paths)
