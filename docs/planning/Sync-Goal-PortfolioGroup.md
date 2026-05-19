# Sync coverage for Goal + PortfolioGroup

**Status:** ✅ shipped 2026-05-19
**Last updated:** 2026-05-19
**Owners:** assistant
**Related:** `Sync-Status-Indicator.md`, `Portfolio-Groups-Refactor.md`

## Why this exists

Two domains don't ride the sync pipeline yet:
- `FinancialGoal` — added in v0.x, sync never wired
- `PortfolioGroup` — added in Portfolio-Groups-Refactor P1, sync intentionally
  deferred

Result: a user with multi-device setup will lose their goals + group
bucket definitions when they switch machines. The recently-shipped sync
status popover also can't reflect these domains' pending count.

This pass closes that gap so all 14 user-mutable domains participate in
sync uniformly.

## Task checklist

- [x] **G1** — Add sync columns to `financial_goal` table via
  `GoalSchemaMigrator`: `version INTEGER DEFAULT 0`,
  `last_modified_at TEXT DEFAULT ''`,
  `last_modified_by_device TEXT DEFAULT ''`, `is_deleted INTEGER DEFAULT 0`,
  `is_pending_push INTEGER DEFAULT 0`. Idempotent via
  `SqliteSchemaHelper.MigrateAddColumn`.
- [x] **G2** — Same for `portfolio_group` via
  `PortfolioGroupSchemaMigrator`.
- [x] **G3** — `IFinancialGoalSyncStore` interface + `GoalSyncMapper`
  (JSON envelope round-trip).
- [x] **G4** — `IPortfolioGroupSyncStore` + `PortfolioGroupSyncMapper`.
- [x] **G5** — Implement `IFinancialGoalSyncStore` on
  `GoalSqliteRepository`: `GetPendingPushAsync` / `MarkPushedAsync` /
  `ApplyRemoteAsync`. Mutate `version` + sync flags on every
  Add/Update/Remove.
- [x] **G6** — Same for `PortfolioGroupSqliteRepository`.
- [x] **G7** — `FinancialGoalLocalChangeQueue` +
  `PortfolioGroupLocalChangeQueue`.
- [x] **G8** — Register both queues in `CompositeLocalChangeQueue` map
  inside `AddAssetraSync`.
- [x] **G9** — Add `"FinancialGoal"` and `"PortfolioGroup"` to
  `SqlitePendingPushCounter.Targets` so the status popover shows their
  rows.
- [x] **G10** — Add lang keys `Sync.Domain.FinancialGoal` /
  `.PortfolioGroup` to zh-TW + en-US.
- [x] **G11** — Tests: sync mapper round-trip + repo
  pending/mark/apply flow for both domains (4 tests minimum).
- [x] **G12** — Build + targeted tests + commit + doc final update.

## Acceptance

- Adding/editing a Goal or PortfolioGroup raises pending count on next
  poll cycle.
- Pushing through `BackgroundSyncService` clears them and `is_pending_push`
  flips to 0 in the DB.
- Sync popover shows 14 domain rows (was 12), including 「財務目標」
  and 「投資群組」.
- Tests pass; existing sync tests still pass.
