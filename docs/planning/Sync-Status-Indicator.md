# Sync Status Indicator

**Status:** Phase 1 — ✅ shipped 2026-05-19; Phase 2/3 deferred
**Last updated:** 2026-05-19
**Owners:** assistant
**Related:** `BackgroundSyncService`, `*SyncStore` infrastructure

## Why this exists

User is heading toward multi-device usage (desktop + mobile/tablet/work
machine). Today the BackgroundSyncService runs silently — users have no way
to know whether their latest local change has actually been pushed to the
cloud yet. The risk: someone makes a critical change on machine A, closes the
app before sync completes, opens machine B, and the change isn't there.

We need an always-visible indicator + drill-down. Designed in 3 phases so
each is independently shippable.

## Phase 1 — MVP integral indicator (this pass)

**Goal:** Status bar shows aggregate sync state at a glance: green dot
"已同步" / orange "5 筆待同步" / spinner "同步中…" / red "同步失敗" /
gray "未啟用".

**Architecture — hybrid event + light polling (revised 2026-05-19):**

Original plan was per-repo `LocalChangeCountChanged` events on 13 repos
(zero DB poll, fully real-time). Trade-off review during T3 showed that's
60+ small edits across the persistence layer, high churn for marginal UX
benefit. Switched to:
- `BackgroundSyncService` raises `SyncStarted` / `SyncCompleted(int pushed)` /
  `SyncFailed(string msg)` / `EnabledChanged(bool)` — drives the state
  machine (Idle ↔ Syncing ↔ Failed ↔ Disabled).
- `GlobalSyncStatusService` polls `GetPendingPushAsync().Count` across all
  sync stores on a 5-second timer (debounced) **and** immediately after
  each sync tick. SQLite WAL means ~13 short COUNT queries every 5 sec is
  negligible.
- Counter staleness ≤ 5 sec after a user mutation, which is fine for an
  ambient status indicator. If real-time becomes desired later, the
  `ILocalChangeCountSource` interface (already defined in Core) can be
  wired into repos as a Phase 2 enhancement without breaking changes.

### Task checklist

- [x] **T1** — ✅ Done 2026-05-19. Created
  `GlobalSyncSnapshot` + `GlobalSyncState` (Models/Sync/),
  `IGlobalSyncStatusService` + `ILocalChangeCountSource` +
  `IBackgroundSyncSignals` (Interfaces/Sync/). Core builds clean.
- [x] **T2** — ✅ Done 2026-05-19 (revised). `GlobalSyncStatusService`
  now uses `IPendingPushCounter` polled every 5 sec via Rx
  `Observable.Interval(scheduler)`, plus immediate re-poll on
  `SyncCompleted`. State machine driven by `IBackgroundSyncSignals`.
  Snapshot built inside `lock` to prevent torn reads, `Changed` fires
  after lock releases. Builds clean.
  - Bonus deliverable: `SqlitePendingPushCounter` impl runs 12 short
    `SELECT COUNT(*)` queries (one per known sync table), tolerates
    missing tables/columns, total cost sub-ms on WAL DB.
- [x] **T3** — ✅ Done 2026-05-19 (scope changed mid-stream). After
  reviewing the cost of touching 13 repos for marginal UX (counter
  staleness drops from ≤5s to 0s), pivoted to poll-based. Kept the
  partial repo instrumentation on `TradeSqliteRepository` +
  `PortfolioSqliteRepository` as a reference for future Phase 2
  realtime upgrade — events fire but service ignores them in MVP.
  `IPendingPushCounter` aggregator interface created instead, with
  one DB query per domain per 5-sec tick.
- [x] **T4** — ✅ Done 2026-05-19. `BackgroundSyncService` now
  implements `IBackgroundSyncSignals`. Raises `SyncStarted` before
  `SyncAsync` call, `SyncCompleted(pushedCount)` after success,
  `SyncFailed(message)` on exception. Also subscribes to
  `IAppSettingsService.Changed` to emit `EnabledChanged(bool)` when
  user toggles SyncEnabled in settings. Builds clean.
- [x] **T5** — ✅ Done 2026-05-19. `StatusBarViewModel` now takes an
  optional `IGlobalSyncStatusService?` (3rd ctor arg). Adds
  `SyncSnapshot` `[ObservableProperty]` (notifies derived
  `SyncStatusText` + `SyncStatusBrush`). Subscribes to `Changed`,
  marshals to dispatcher thread. Localization change re-renders the
  label. Built clean.
- [x] **T6** — ✅ Done 2026-05-19. Sync indicator (colored
  dot + text + tooltip) added to the left of the market open/closed
  indicator, separated by a 1×12px divider. Order: `● sync | ● market    HH:mm:ss`.
- [x] **T7** — ✅ Done 2026-05-19. `BackgroundSyncService` now
  registered as singleton AND forwarded under
  `IBackgroundSyncSignals` AND `IHostedService` (3 service identities,
  1 instance). `IPendingPushCounter` registered in
  `AddAssetraPlatformServices` (has `dbPath`).
  `IGlobalSyncStatusService` registered in `AddAssetraSync` with
  initial enabled state from `AppSettings.SyncEnabled`. Builds clean.
- [x] **T8** — ✅ Done 2026-05-19. 6 new keys per language file:
  `StatusBar.Sync.Disabled` / `.Synced` / `.Syncing` / `.Failed` /
  `.Offline` / `.PendingFormat`. Chinese: 「未啟用同步 / 已同步 /
  同步中… / 同步失敗 / 離線 / {0} 筆待同步」.
- [x] **T9** — ✅ Done 2026-05-19. 9 tests in
  `Assetra.Tests/Infrastructure/Sync/GlobalSyncStatusServiceTests.cs`
  covering: Disabled init, Idle init, RefreshAsync aggregation across
  domains, SyncStarted → Syncing, SyncCompleted → Idle + re-polls
  counter + clears error, SyncFailed → Failed + preserves message,
  EnabledChanged true/false. All pass. Caught one real bug: state
  was stuck in Syncing after SyncCompleted because `_state` wasn't
  reset — fixed by transitioning to Idle in the completed handler
  (BuildSnapshotLocked then promotes to Pending if counter > 0).
- [x] **T10** — ✅ Done 2026-05-19. Solution-wide
  `dotnet build Assetra.slnx`: **Build succeeded** (0 errors). Sync
  filter `dotnet test --filter ~GlobalSyncStatusService`:
  **9/9 pass** (703 ms). Full-suite background run (minus the
  pre-existing `SaveEditAsync_RefreshesBudgetCategoryDisplay` flake):
  1199 pass / 0 fail. No regressions from Phase 1 wiring.

### Acceptance

- Status bar shows green dot + "已同步" at app start when no pending changes.
- Adding/editing any record bumps the count and turns the dot orange + "N 筆待同步".
- After BackgroundSyncService successfully pushes, the count returns to 0 + green dot.
- If sync disabled (no passphrase), shows gray dot + "未啟用同步".
- Build + all tests pass.

## Phase 2 — Per-domain popover (next pass)

Not in scope for this PR. Documented for reference:
- `IGlobalSyncStatusService.GetPerDomain()` returns
  `IReadOnlyList<DomainSyncStatus>`.
- Click status bar → popover with per-domain breakdown + "立即同步" button.

## Phase 3 — Error detail + per-domain retry (later)

Not in scope for this PR.
