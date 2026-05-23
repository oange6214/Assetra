# Transaction FX Settlement

This document defines how Assetra records foreign-stock transactions when the instrument currency and the cash settlement currency are different.

## User-Facing Terminology

- **Trade currency / 成交幣別**: the currency of the instrument price and trade amount.
- **Trade amount input / 成交金額輸入方式**: whether the user enters per-share price or total trade amount.
- **Cash settlement / 扣款與匯率**: the cash-account movement when the selected cash account currency differs from the trade currency.
- **Actual cash debit or credit / 實際扣款或入帳金額（依券商或帳戶明細）**: the broker or account statement amount.
- **Settlement input mode / 扣款資料來源**: whether the statement amount or an FX estimate is authoritative for the cash movement.

Authority order:

1. In statement mode, the broker/account statement actual cash amount is authoritative.
2. In FX estimate mode, the explicit FX rate entered or fetched for the trade date is authoritative.
3. Same-currency trades use price, quantity, and fee directly and do not show cash-settlement inputs.

FX is metadata and estimation support unless the user explicitly chooses FX estimate mode.

## Core Rule

Trade price and quantity stay in the instrument currency. Cash movement stays in the selected cash-account currency.

For example, buying a US ETF with a TWD cash account is recorded as:

- Instrument currency: `USD`
- Settlement currency: `TWD`
- Unit price: USD price per share
- Quantity: shares
- Actual cash amount: the TWD amount paid from the cash account, when statement mode is used
- FX rate/date/source: audit metadata used to explain or estimate the settlement

Trade currency alone does not imply the broker path. `USD` can mean a USD brokerage account, a foreign-currency bank account, or a TWD-funded trade. The selected cash account currency determines whether the trade is cross-currency.

`ActualCashAmount` is the audit source of truth only in statement mode. `FxRate` is the source of truth only in FX estimate mode.

## Trade Fields

The trade journal stores these currency-related fields:

- `InstrumentCurrency`: currency used by the instrument price.
- `SettlementCurrency`: currency used by the cash movement.
- `FxRate`: instrument currency to settlement currency rate.
- `FxRateDate`: date of the FX rate used at entry time.
- `FxSource`: provider or `manual`.
- `CashAmount`: actual cash settlement amount in settlement currency.

Legacy rows without `SettlementCurrency` default to `TWD`.

## Transaction Entry Behavior

Same-currency trades do not require FX. If the selected cash account currency matches the trade currency, the trade amount plus fee is the cash movement.

Cross-currency buy entry shows a settlement section with:

- instrument currency
- settlement currency
- settlement input mode
- actual cash amount in statement mode
- FX rate, FX rate date, FX source, and refresh action in FX estimate mode

Market-price fetch and FX fetch are separate actions:

- Market price fills the unit price.
- FX fetch fills the FX rate, rate date, and source when FX estimate mode is selected.

If FX is unavailable, the user can switch to statement mode and enter the actual cash amount, or manually enter the FX rate in FX estimate mode.

## Editing Behavior

Editing an existing trade must restore:

- actual cash amount
- instrument currency
- settlement currency
- FX rate
- FX rate date
- FX source

Saving the edited trade must update the original trade rather than creating a duplicate. The side-panel transaction list displays a compact FX summary only for cross-currency trades.

## Reporting Boundary

Reports may display values in TWD using available FX data. When historical FX is missing, report values may be estimates or unavailable depending on the report. This trade-entry contract does not define realized PnL methodology; report accuracy changes should be handled in a separate reporting task.
