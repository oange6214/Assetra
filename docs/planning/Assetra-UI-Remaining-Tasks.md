# Assetra UI — 剩餘 task list

依序執行，每完成一項標 ✅ + commit。
最後更新：2026-05-21 (P2.16 完工後盤點)

---

## 🟢 High CP (1-2h each)

- [ ] **T01** — FIRE widget 4 metrics（預估自由日期 / 進度 / 投入金額 / 成功率）
  - FireViewModel 加 `FireDateDisplay` / `ProgressPercent` / `MonthlyContributionDisplay` / `SuccessRate` computed properties
  - FireWidget.xaml 在 HasCalculatedResult=true 區塊展開 metrics rows
- [ ] **T02** — Skeleton 擴展剩餘 Summary tier cards（總負債 / 淨資產 mini / 資產組成）
- [ ] **T03** — Per-type TxForms label margin 4 → 6（Sell/CashDiv/StockDiv/CashFlow/Income/Loan/CreditCard/Transfer 共 8 forms）
- [ ] **T04** — Keyboard shortcuts help dialog（Ctrl+/）— 列現有快捷鍵
- [ ] **T05** — Tooltip 系統一致化（AppTooltip style）
- [ ] **T06** — Snackbar styling audit
- [ ] **T07** — Recent commands in Command Palette（LRU track）
- [ ] **T08** — Reduced-motion preference 處理（Skeleton 動畫尊重系統偏好）
- [ ] **T09** — 既存硬碼 hex 收編（Reports warning/info banner + Calendar overlay）

## 🟡 Medium (0.5-1 day)

- [ ] **T10** — AppCard* mechanical migration（DashboardWidgetCard alias 改 widget XAML 直接用 AppCardSubtle）
- [ ] **T11** — Empty state per-page application check（pages 是否實際套了 EmptyState style）
- [ ] **T12** — Localization key 完整 audit（漏譯 / typo / en-US 漢字 fallback）
- [ ] **T13** — Search popup ↔ Command Palette visual 統一

## 🔴 大型（標記但不在這輪做）

- ⏸ **T20** — Sync engine literal「N 個帳戶」（需 schema migration + 6-8 producer site，[文件已存](Sync-Account-Count-Tracking.md)）
- ⏸ **T21** — Animation / transition 系統（page transition / card 出現 / number tween，1 天）
- ⏸ **T22** — Accessibility audit（鍵盤 nav / screen reader / WCAG，1-2 天）
- ⏸ **T23** — Light theme 實機 toggle 跑一輪（需實機）
- ⏸ **T24** — Per-page deep audit: TransactionLog / Reconciliation / Trends / Loan dialog / Account dialog / Liability dialog / Categories budget（需實機觀察痛點）

---

## 完成日誌

(每完成一項 append 一行：`✅ Txx — 簡述 (commit hash)`)
