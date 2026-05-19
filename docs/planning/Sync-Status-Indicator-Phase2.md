# Sync Status Indicator — Phase 2

**Status:** ✅ shipped 2026-05-19
**Last updated:** 2026-05-19
**Owners:** assistant
**Related:** `Sync-Status-Indicator.md` (Phase 1 shipped 2026-05-19)

## Why this exists

Phase 1 surfaces an aggregate count + state in the status bar. User feedback
during Phase 1 design called for drill-down: when there are pending changes,
clicking the indicator should reveal **which domain** has unsynced items
and offer an "立即同步" button. Without this, the indicator only answers
"is anything pending?" — not "what?" or "how do I push it now?".

## Goal

Click status bar → small popover (anchored above the indicator) listing
each tracked domain with:
- 中文化 domain name (交易記錄 / 投資資產 / 收支分類 / ...)
- Per-domain status (✓ 已同步 / ⟳ 同步中 / ⏸ 待同步 N)
- Per-domain pending count
- Top-level "立即同步" button (calls `BackgroundSyncService` immediate path)
- Top-level "上次同步 yyyy-mm-dd HH:mm" timestamp

## Task checklist

- [x] **P2.T1** — Extend `IGlobalSyncStatusService`: add `GetPerDomain()`
  returning `IReadOnlyList<DomainSyncStatus>`. New record
  `DomainSyncStatus(string DomainKey, int PendingCount, bool IsSynced)`
  in `Assetra.Core/Models/Sync/`.
- [x] **P2.T2** — Implement `GetPerDomain()` in `GlobalSyncStatusService`
  using the existing in-memory per-domain dict (already maintained by
  `PollAsync` via `IPendingPushCounter`). No new DB query.
- [x] **P2.T3** — Add `TriggerImmediateSyncAsync()` to
  `IBackgroundSyncSignals` (or a sibling interface) that lets the
  popover request an out-of-band sync push.
  Implementation in `BackgroundSyncService` signals the existing wait
  loop via a `ManualResetEventSlim` / `CancellationTokenSource` so the
  next tick runs immediately.
- [x] **P2.T4** — New `SyncStatusPopoverViewModel` in
  `Assetra.WPF/Features/StatusBar/`. Subscribes to
  `IGlobalSyncStatusService.Changed`, exposes
  `ObservableCollection<DomainSyncRow>` (with localized names + status
  emoji + count). Wires `TriggerSyncCommand` to the immediate sync path.
- [x] **P2.T5** — New `SyncStatusPopoverView.xaml` — small Popup-based
  panel (rounded card, ~280px wide), title "同步狀態" + per-domain rows
  + footer with timestamp + button.
- [x] **P2.T6** — `StatusBarView.xaml`: convert the sync indicator
  StackPanel into a clickable Button (hover state + InputBindings.Click)
  that opens the popover via toggled Popup `IsOpen`.
- [x] **P2.T7** — DI registration + lang strings (per-domain labels):
  `Sync.Domain.Trade` / `.Portfolio` / `.Asset` / `.Category` /
  `.Recurring` / `.Goal` / etc. — 中文 / 英文.
- [x] **P2.T8** — Tests: extend `GlobalSyncStatusServiceTests` with
  `GetPerDomain_ReturnsAllRegisteredDomains` + immediate-sync trigger
  flowthrough.
- [x] **P2.T9** — Build + targeted test run + plan doc final update.

### Acceptance

- Status bar sync indicator becomes hoverable + clickable (cursor `Hand`).
- Click opens a popover anchored above the indicator showing 10+ domain
  rows, each with its localized name + ✓/⏸/⟳ icon + pending count.
- Click outside the popover closes it.
- "立即同步" button calls into `BackgroundSyncService` which fires
  `SyncStarted` event immediately (no 30-sec wait), and the popover's
  "上次同步" line updates after.
- Tests pass; existing Phase 1 tests still pass.
