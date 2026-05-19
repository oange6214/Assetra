# Multi-Currency Trade Refactor

**Status:** Planning / playbook
**Last updated:** 2026-05-12
**Owners:** unassigned

## Why this exists

The 買入交易 dialog currently treats **「成交價 × 數量」** and **「實際扣款金額」** as competing inputs with no clear semantic split. For 一般台股 (TWD instrument paid from TWD account) the two values coincide, so the confusion is invisible. The moment a user enters a **複委託** trade (USD instrument paid from TWD account) or any cross-currency situation, the dialog forces them to:

1. Either compute USD-per-share from a TWD debit notification (manual FX math)
2. Or leave 實際扣款金額 blank and accept that the cash account balance will be off by FX delta + foreign fees

Neither path is correct. The underlying issue is that `Trade` has no notion of **instrument currency separate from cash account currency**, so the dialog cannot record FX context. This doc lays out the data-model + UI refactor needed to support:

- 台股 (TWD instrument × TWD cash account) — the current happy path
- 美股 (USD instrument × USD cash account, e.g. Interactive Brokers)
- 複委託 台幣購買 (USD instrument × TWD cash account, e.g. 富邦複委託)
- 複委託 美金購買 (USD instrument × USD-denominated 複委託 sub-account)
- 港股 / 日股 / 其他外幣標的 — same shape as 複委託

## Core conceptual model (three orthogonal axes)

Every trade has three axes the data model must keep separate:

| Axis | Example | Stored as |
|------|---------|-----------|
| **Instrument currency** | 2330=TWD, AAPL=USD, 7203.T=JPY, 0700.HK=HKD | `Trade.InstrumentCurrency` (new) |
| **Funding currency** | TWD account, USD account | `AssetItem.Currency` (already exists, of `CashAccountId`) |
| **Settlement amount** | actual cash debit after FX + fees | `Trade.CashAmount` (already exists) |

When `InstrumentCurrency == FundingCurrency` (台股 + TWD, or 美股 + USD), FX is implicit 1.0 and `CashAmount` is computable. When they differ, the trade record must carry an explicit `FxRate` **or** an explicit `CashAmount` so that:

```
CashAmount ≈ Price × Quantity × FxRate + Commission × FeeFxRate
```

is reconcilable. The user fills any two of {`Price`, `FxRate`, `CashAmount`} and the system derives the third.

→ **Key insight:** 複委託 is not a special transaction type. It is the general case `InstrumentCurrency ≠ FundingCurrency`, and the existing TWD-domestic path is the degenerate special case where they happen to be equal.

## Data model changes

### `Trade` record extensions (Phase 1)

```csharp
public sealed record Trade(
    // ... existing fields ...
    decimal Price,              // ⚠ semantics tightened: per share in InstrumentCurrency
    decimal? Commission,        // unchanged; in CommissionCurrency
    decimal? CashAmount,        // unchanged; in funding currency (cash account)

    // New fields (all additive, all optional with sensible defaults)
    string InstrumentCurrency = "TWD",  // ISO 4217; matches Currency.Code (existing VO)
    string? CommissionCurrency = null,  // null → same as InstrumentCurrency
    decimal? FxRate = null              // InstrumentCcy → FundingCcy; null → 1.0 (same ccy)
);
```

**Defaults are chosen so that all existing TWD trades remain semantically correct without backfill**. The migration in P2 will set `InstrumentCurrency` to the symbol's actual currency, but until then everything is "TWD" and `FxRate` is implicitly 1.

### SQLite columns (Phase 1)

Three new nullable columns on `trade` table, added via `TradeSchemaMigrator.MigrateAddColumn`:

| Column | Type | Default | Notes |
|--------|------|---------|-------|
| `instrument_currency` | `TEXT NOT NULL DEFAULT 'TWD'` | `'TWD'` | ISO 4217 code |
| `commission_currency` | `TEXT` | NULL | NULL means inherits `instrument_currency` |
| `fx_rate` | `REAL` | NULL | NULL means 1.0 (same currency) |

Add to `TradeSchemaMigrator.AllowedColumns` and the corresponding `AllowedTypes`. No new index needed (queries don't filter by currency).

### Sync mapper (Phase 1)

`TradePayloadDto` gains three optional JSON fields (`instrument_currency`, `commission_currency`, `fx_rate`). All emitted as strings (invariant culture) for `fx_rate`, matching existing decimal-as-string pattern that avoids JSON double round-trip drift. Existing cloud payloads omitting these fields decode safely thanks to defaults.

### Currency resolution (Phase 2)

**Updated 2026-05-13:** Initially this section proposed a new `ExchangeCurrencyResolver` in `Assetra.Core`. During implementation we discovered `Assetra.Core.Models.StockExchangeRegistry.ResolveDefaultCurrency(exchange)` already existed with the exact same mapping (TWSE/TPEX→TWD, NYSE/NASDAQ/NYSEARCA/AMEX/BATS/IEX→USD, HKEX→HKD, TSE→JPY). The redundant new helper was deleted and we use the registry as the single source of truth.

The lookup is used in three places — keep them aligned with the registry:
1. **Trade creation** (Application workflow services) — auto-fill `Trade.InstrumentCurrency` from `(Symbol, Exchange)`
2. **One-time backfill** (`TradeSchemaMigrator.BackfillInstrumentCurrencyFromExchange`) — populate `instrument_currency` on existing rows via inline `CASE UPPER(exchange) WHEN ...` SQL (the registry mapping inlined for SQLite; verify both lists match when adding a venue)
3. **UI mode detection** (Phase 3) — `TransactionDialogViewModel.ResolveCurrentInstrumentCurrency` reads `AddSymbolCurrency` then falls back to `StockExchangeRegistry.ResolveDefaultCurrency(AddExchange)`

## Phase plan

### P1 — Model + persistence extension (low risk, no UI change)

**Goal:** Trade record can store FX context, but UI / dialogs / reports still behave exactly as today.

**Files touched:**
- `Assetra.Core/Models/Trade.cs` — append 3 fields with defaults
- `Assetra.Infrastructure/Persistence/TradeSchemaMigrator.cs` — 3 `MigrateAddColumn` calls + allowlist entries
- `Assetra.Infrastructure/Persistence/TradeSqliteRepository.cs` — extend `SelectClause`, `MapTrade`, `BindTradeParams`, `InsertSql`, `UpdateAsync` UPDATE, sync upsert SQL
- `Assetra.Infrastructure/Sync/TradeSyncMapper.cs` — extend `TradePayloadDto` + `ToEnvelope` + `FromPayload`
- `Assetra.Tests/Infrastructure/TradeSyncMapperTests.cs` — add round-trip test for new fields + back-compat test (old JSON without new fields decodes with defaults)

**Acceptance:**
- Existing tests pass without modification
- New tests: round-trip a `Trade` with `InstrumentCurrency="USD"`, `FxRate=31.5`, verify SQLite + cloud sync preserve values
- DB upgrade from v0.x schema (no new columns) succeeds with `instrument_currency='TWD'` on all existing rows
- No behavioral change anywhere in the app

### P2 — Symbol → currency detection + backfill (low risk, derives defaults)

**Goal:** Trade records correctly reflect the actual instrument currency, both for new trades and existing rows.

**Files touched:**
- `Assetra.Infrastructure/History/ExchangeCurrencyResolver.cs` — new file (~30 LOC)
- `Assetra.Infrastructure/Persistence/TradeSchemaMigrator.cs` — add `BackfillInstrumentCurrencyFromExchange` step that UPDATEs trade rows where `instrument_currency = 'TWD'` AND `exchange` is one of the foreign venues. Idempotent.
- `Assetra.WPF/Features/Portfolio/SubViewModels/TransactionDialogViewModel.Confirm.*.cs` — at Trade construction, set `InstrumentCurrency = ExchangeCurrencyResolver.Resolve(exchange)`
- `Assetra.Tests/Infrastructure/ExchangeCurrencyResolverTests.cs` — new unit tests
- `Assetra.Tests/Infrastructure/TradeSchemaMigratorTests.cs` — verify backfill assigns USD to NYSE rows, etc.

**Acceptance:**
- All Trade rows in DB have correct `instrument_currency` matching their `exchange`
- New Buy / Sell trades inherit the correct currency automatically (no UI involvement)
- No user-visible change yet — FxRate is still null, CashAmount still flows through old code path

### P3 — Cross-currency detection + FX rate field on BuyTxForm (medium risk, big UX win)

**Status (2026-05-13):** P3.A delivered — automatic detection of cross-currency situations with a banner + FX-rate input field. P3.B (full three-mode UI + Sell side) deferred to a future iteration.

**Goal:** Replace the current `PriceMode = unit/total` toggle + opaque `ActualCashAmount` escape hatch with three explicit modes, auto-detected from `(InstrumentCurrency, CashAccount.Currency)`.

**Mode A — 本幣交易 (same currency)**
- Trigger: `InstrumentCurrency == CashAccount.Currency`
- Visible fields: `Price`, `Quantity`, `Commission` (auto or manual), `CashAccount`
- Hidden: `FxRate`, `ActualCashAmount` (always computed)
- Use case: 台股 + TWD 帳戶, 美股 + USD 帳戶

**Mode B — 跨幣別交易 (cross currency)**
- Trigger: `InstrumentCurrency != CashAccount.Currency`
- Visible fields: `Price` (instrument ccy), `Quantity`, **either** `FxRate` **or** `ActualCashAmount` (the other auto-fills via constraint solver), `Commission` + `CommissionCurrency`
- Use case: 複委託 美股 + TWD 帳戶, 港股 + TWD 帳戶

**Mode C — 快速輸入 (settlement-only)**
- Trigger: explicit user toggle (advanced)
- Visible fields: `Symbol`, `Quantity`, `ActualCashAmount`, `CashAccount`
- Backsolve: `Price = (ActualCashAmount − estimatedFee) / Quantity / FxRate`; FX rate fetched from `IFxRateService` for the trade date (or live rate)
- Use case: lazy entry from "你扣款 NT$138,581" banking notification

**Files touched (largest scope of the refactor):**
- `Assetra.WPF/Features/Portfolio/SubViewModels/Tx/BuyTxViewModel.cs` — replace `PriceMode` with `TxMode` enum {SameCurrency, CrossCurrency, SettlementOnly}; add `FxRate`, `EffectiveInstrumentCurrency` properties; constraint solver between FxRate / CashAmount
- `Assetra.WPF/Features/Portfolio/Controls/TxForms/BuyTxForm.xaml` — three nested `StackPanel`s with `Visibility` bound to mode; existing fields move into Mode A
- `Assetra.WPF/Features/Portfolio/SubViewModels/Tx/SellTxViewModel.cs` — mirror changes (sell is symmetric — proceeds in instrument ccy, cash account credit in funding ccy)
- `Assetra.WPF/Features/Portfolio/Controls/TxForms/SellTxForm.xaml` — mirror
- `Assetra.WPF/Languages/zh-TW.xaml` + `en-US.xaml` — new string keys for mode labels, FX rate label, settlement-only mode hint

**Acceptance:**
- Selecting a TWD symbol with a TWD cash account auto-defaults to Mode A — UI identical to today's "單價" mode
- Selecting a USD symbol with a TWD cash account auto-defaults to Mode B — FX rate field appears
- Editing an existing trade auto-detects the correct mode from its stored fields
- Round-trip test: enter trade in Mode B → save → reopen → fields restore exactly

### P4 — Multi-currency reports + chart (high risk, deferred)

**Goal:** Position cost, valuation, return %, benchmark comparison all become currency-aware. User can switch reporting basis (TWD / USD / per-position) globally or per-view.

**Out of scope for this doc.** Will get its own planning doc (`Multi-Currency-Reporting.md`) once P1–P3 lands and there's enough cross-currency data to validate aggregation.

## Open questions

1. **Commission currency for 複委託**: Some brokers charge 海外手續費 in USD; some in TWD. Need to confirm with a real example from 富邦/永豐 統一 sub-brokerage. P3 model assumes `CommissionCurrency` is independent of `InstrumentCurrency` — this is the most general but possibly over-engineered. Could simplify to "always same as InstrumentCurrency" if real-world data supports it.

2. **FX source for Mode C backsolve**: Should the rate come from `IFxRateService.GetRateAsync(trade.TradeDate)` (historical) or live? Historical is more accurate but requires a daily FX history table. For v1 of P3, use live rate as approximation and surface a "rate as of today" hint.

3. **Sell side FX**: When 賣出 a USD-priced stock into a TWD account, the same FX axis applies. P3 must mirror these changes in `SellTxViewModel`. CashDividend has the same shape (USD dividend deposited as TWD).

4. **Existing `ActualCashAmount` semantics**: Today it means "override the auto-computed cash deduction". After P3 it has the same semantic for Mode A, but in Mode B/C it becomes the cross-currency settlement record. Backward-compatible because the field's behavior on the underlying `Trade.CashAmount` is unchanged.

## Risk + rollback

- **P1 + P2 risk: low.** Pure additive — old code paths untouched, defaults chosen so existing rows remain semantically correct. Rollback = revert commits; DB columns stay but become unused (no NULL/NOT NULL strictness on the new columns).
- **P3 risk: medium.** Touches the most-used dialog in the app. Mitigation: keep `PriceMode` field on `BuyTxViewModel` but mark obsolete; new code reads `TxMode` only. Old serialized state from edit-resume scenarios deserialize cleanly via positional defaults.
- **DB downgrade**: SQLite ignores unknown columns on older builds — if user reverts the app version, the new columns just sit dormant. New columns are nullable / have defaults so older code can keep INSERTing into the same table.
