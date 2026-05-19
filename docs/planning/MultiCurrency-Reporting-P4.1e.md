# MultiCurrency-Reporting P4.1e — Hybrid FX Rate Provider

**Status:** ✅ shipped 2026-05-19
**Last updated:** 2026-05-19
**Owners:** assistant
**Parent:** P4.1 (✅) + P4.1b/c/d (✅)

## Why this exists

P4.1 builds + populates `fx_rate_history`, but **nothing reads from it**.
Existing callers (`MultiCurrencyValuationService` → `BalanceSheetService`,
`ConcentrationAnalyzer`, etc.) inject `IFxRateProvider`, which is wired to
the legacy `StaticFxRateProvider`/`fx_rate` table. The result: all the
historical FX data we just built is invisible to actual reports.

This pass adds a hybrid decorator: try the historical store first, fall
back to the legacy provider when nothing is found. Zero callers change.

## Scope

In:
- `HybridFxRateProvider : IFxRateProvider` — decorator wrapping the new
  history service + the legacy provider. Same interface, different priority.
- DI: re-register `IFxRateProvider` to resolve to the hybrid. Keep
  `StaticFxRateProvider` registered under its concrete type so the
  hybrid can inject it.
- Tests covering: history hit, history miss + legacy hit, both miss → null,
  same-currency short-circuit.

Out:
- Refactoring `MultiCurrencyValuationService` directly (decorator pattern is
  zero-blast-radius)
- Changing the `fx_rate` legacy table — keeps user-entered manual rates as
  authoritative override

## Task checklist

- [x] **H1** — `Assetra.Infrastructure/Fx/HybridFxRateProvider.cs` impl.
- [x] **H2** — DI re-registration in `ServiceCollectionExtensions`.
- [x] **H3** — Tests with mocked history + legacy providers covering all 4
  combinations.
- [x] **H4** — Build + commit + plan doc final.

## Acceptance

- Open balance sheet for past date → values convert at that day's FX rate
  (now sourced from `fx_rate_history` populated by P4.1c startup pull).
- If history has no data for that date, falls back to legacy live rate.
- If neither has data, returns null (BalanceSheet warnings banner fires).
- All existing callers transparently benefit without code change.
