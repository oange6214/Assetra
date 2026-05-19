# MultiCurrency-Reporting P4.1d — Settings UI for FX Refresh

**Status:** ✅ shipped 2026-05-19
**Last updated:** 2026-05-19
**Owners:** assistant
**Parent:** P4.1c (✅ shipped)

## Why this exists

P4.1c runs the refresher silently on startup. User has zero visibility
into whether it ran, when, or what's in the table. This pass surfaces it.

## Scope

In:
- `AppSettings.LastFxRefreshAt: DateTimeOffset?` (persisted)
- `FxRateHistoryRefresher` writes the timestamp after a successful refresh
  (= upserted at least one row)
- Settings page: small "匯率歷史" section with:
    * 「上次更新：yyyy-mm-dd HH:mm」 or 「尚未更新過」
    * 「立即更新」 button → calls refresher, shows spinner while running

Out:
- Daily timer while app running — defer until proven needed
- Per-currency-pair management UI — DefaultForeignCurrencies is fine for v1
- Manual rate entry — defer

## Task checklist

- [x] **D1** — Add `LastFxRefreshAt` to `AppSettings` + JSON serialization.
- [x] **D2** — `FxRateHistoryRefresher` takes an optional callback /
  `IAppSettingsService` to update timestamp on successful upsert.
- [x] **D3** — VM exposing `LastFxRefreshDisplay` + `RefreshFxCommand` +
  `IsRefreshingFx`. Either extend `SettingsViewModel` directly or add a
  small sub-VM.
- [x] **D4** — XAML: add the "匯率歷史" section to `SettingsView.xaml`.
- [x] **D5** — Lang keys (zh + en): section title, "立即更新", "上次更新",
  "尚未更新", "更新中…".
- [x] **D6** — Tests: VM command toggles IsRefreshing during run +
  invokes refresher.
- [x] **D7** — Build + commit + plan doc final update.

## Acceptance

- Open settings → see "匯率歷史" section with timestamp.
- Click "立即更新" → button disables, "更新中…" shows, returns when done.
- Timestamp updates to current time after success.
- App restart preserves the timestamp.
