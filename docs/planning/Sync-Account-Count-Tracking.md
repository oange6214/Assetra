# Sync Account Count Tracking — Deferred Notes

## Audit reference

UX/UI review document audited Status Bar as 7/10 and suggested rich-data
combinations:

> 同步完成 · 3 個帳戶已更新
> 市場已收盤 · 今日淨值 +0.82%
> 資料更新於 2026-05-20 23:34
> 離線模式 · 使用本機資料

P2.12 — copy soften pass (「市場已收盤」「同步未開啟」「離線模式」)
P2.13 — today's P&L % chip + LastSyncedAt timestamp tail (`已同步 · 上次同步 14:23`)
P2.14 batch O — added LastPushedCount → `已同步 · 推送 N 筆`

The literal "**3 個帳戶**" form was deliberately deferred. This document
explains why and what would be needed to make it work.

## Why row count, not account count

Assetra's sync engine is **row-oriented** (sync_outbox table) not
account-oriented:

- Each row in `sync_outbox` represents one entity change (a trade insert,
  a position update, etc.)
- `IBackgroundSyncSignals.SyncCompleted` carries `int pushed` = total rows
  pushed across all domains
- `IPendingPushCounter.CountPendingByDomainAsync` returns per-domain row
  counts, again not per-account

There is no built-in mapping from sync row → account ID. Domains like
`positions`, `trades`, `cash_balance_history` reference accounts but the
sync envelope does not surface that linkage cleanly.

"Account" in the audit's sense most likely meant `CashAccount` — a row in
the `asset_item` table with type=Cash. But syncing a trade can affect
multiple cash accounts (transfer) or none (stock dividend). The audit's
mental model doesn't map cleanly onto our sync schema.

## What it would take

To deliver literal `X 個帳戶已更新`:

1. **Schema**: add an `account_ids` column (text, JSON array) to
   `sync_outbox` OR a sidecar `sync_envelope_touches_account` join table.
2. **Producer side**: every place that writes to `sync_outbox` (trade
   workflow, position upsert, balance recompute, …) must annotate the
   row with the touched account IDs.
3. **Consumer side**: `SyncCompletedArgs` evolves to carry
   `IReadOnlySet<Guid> DistinctAccountIds`; aggregator unions per-domain
   sets.
4. **UI**: `GlobalSyncSnapshot.LastSyncedAccountCount`; StatusBar prefers
   account count over row count when > 0.

Total effort: 1-2 day refactor (schema migration + ~6-8 producer sites +
event signature change + UI plumbing + tests).

## Alternative considered

**Domain count proxy**: track number of distinct **domains** (positions /
trades / balance / etc.) that had pending count > 0 before sync and = 0
after. Implementation purely additive in
`GlobalSyncStatusService.PollAsync` — saves previous `_perDomain`,
diffs, counts. Approx 30-min change.

Rejected because:
- "3 個 domain 已同步" reads as engineer-speak in zh-TW UI (使用者看到「domain」
  反而困惑)
- The audit's intent was account-level (人話 = 「我的銀行帳戶」)，
  domain-level proxy doesn't match
- Misleading: a single account change can spread across multiple domains,
  making domain count > account count

## Current behavior summary

| State              | StatusBar text             | Source         |
|--------------------|----------------------------|----------------|
| Idle + pushed > 0  | `已同步 · 推送 N 筆`       | `LastPushedCount` |
| Idle + recently synced | `已同步 · 上次同步 HH:mm` | `LastSyncedAt`   |
| Idle (fresh)       | `已同步`                   | base label       |
| Pending            | `N 筆待同步 · 上次同步 HH:mm` | `TotalPending` + `LastSyncedAt` |
| Syncing            | `同步中…`                  | base label       |
| Failed             | `同步未成功 · 上次同步 HH:mm` | `LastError` + `LastSyncedAt` |
| Disabled           | `同步未開啟`               | base label       |
| Offline            | `離線模式`                 | base label       |

This is truthful and informative within the engine's natural unit.
"推送 N 筆" tells the user *something happened*. The precise "what" is
visible in the sync popover (per-domain counts).

## Recommendation

If `X 個帳戶` becomes user-priority feedback in the future, do the full
1-2 day schema + producer + plumbing refactor described above. Don't
half-do with domain-count proxy or some heuristic — that creates more
confusion than the current truthful `推送 N 筆`.
