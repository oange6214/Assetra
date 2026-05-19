# 月結報告 Loading UX

**Status:** ✅ shipped 2026-05-19
**Last updated:** 2026-05-19
**Owners:** assistant

## Why this exists

User reported the 月結報告 page appeared completely broken because all 3
statement cards showed empty state while "載入中…" hung at the top — but
the data eventually appeared after a long wait. Root cause turned out to
be slow FX rate provider when the network was flaky. The data layer
silently recovered; the UI gave no signal that something was slow rather
than broken.

Three improvements, ranked by ROI:

## Task checklist

### Improvement 1 — Per-card loading state (must do)
Each statement card shows its own loading indicator instead of relying on
the page-level "載入中". Lets the user see Income load while Balance is
still fetching FX rates.

- [x] **U1** — Add `IsIncomeLoading` / `IsBalanceLoading` / `IsCashFlowLoading` /
  `IsTaxLoading` `[ObservableProperty]` on `ReportsViewModel`. Set true
  before each `await ...GenerateAsync()`, false in `finally`.
- [x] **U2** — Update [ReportsView.xaml](Assetra.WPF/Features/Reports/ReportsView.xaml)
  empty-state TextBlock for each card to a 3-state choice:
  loading spinner / data / empty. Use existing `IsXxxLoading` for the
  loading state, `HasXxxStatement` for data, and the negation of both
  for empty.

### Improvement 2 — Slow-load notice (must do)
After 5 seconds of loading still in progress, show an inline warning:
「資料載入時間較長，可能網路不穩」. Auto-dismisses on completion.

- [x] **U3** — Add `IsSlowLoad` `[ObservableProperty]`. Start a 5-second
  `CancellationTokenSource`-driven `Task.Delay` in `LoadAsync`; if it
  completes before `LoadAsync` finishes, set `IsSlowLoad = true`. Cancel
  on `LoadAsync` finally.
- [x] **U4** — Add a yellow `Border` banner under the period chooser bar
  bound to `IsSlowLoad`, with appropriate language string.

### Improvement 3 — Service-level warnings (recommended)
Make missing FX rates explicit instead of silently dropping conversion.

- [x] **U5** — New `StatementWarning` record carrying `Severity` (Info /
  Warning) + `MessageKey` (lang key) + optional `Detail` string.
- [x] **U6** — Add `Warnings` collection to `BalanceSheet`. Populate when
  `ResolveFxFactorsAsync` returns null factor for a currency that's
  actually present in the data (so warning only surfaces when impact).
- [x] **U7** — Hoist warnings into `ReportsViewModel` (aggregated across
  all 3 statements) + render as an info banner above the per-card sections.

### Acceptance

- Open 月結報告 → if data loads in < 1 sec, no extra UI (no flashing of "載入中" per card).
- Open 月結報告 with FX provider slow (mock test or real slow network):
  - Top page "載入中" still shows.
  - Each card shows its own "載入中" overlay until that service returns.
  - At 5 sec, a slow-load notice appears.
  - When data lands, notice + per-card loading disappears, real data shows.
- If FX rate missing for a currency that has positions, a warning
  banner appears: "匯率資料缺失：[USD], 部分數值未換算到 base currency"
- Build + all relevant tests pass.

### Acceptance test pass criteria

- 1 unit test: `IsSlowLoad` flips true after 5 sec when load doesn't
  finish, flips back false on completion.
- 1 unit test: `BalanceSheet.Warnings` populated when FX provider
  returns null for a currency present in held positions.
