# Assetra UI — 剩餘 task list

依序執行，每完成一項標 ✅ + commit。
最後更新：2026-05-21 (P2.16 完工後盤點)

---

## 🟢 High CP (1-2h each)

- [x] **T01** — FIRE widget 4 metrics（FIRE 目標 / 預估自由年份 / 完成進度 / 累計投入）
  - 「成功率」 audit 提的需要 Monte Carlo 整合，本輪用 FIRE 目標金額替代
- [x] **T02** — Skeleton 擴展剩餘 Summary tier cards（總負債 ✅ / 淨資產 mini ✅ / 資產組成 暫不加 — 它是分項列表複雜）
- [x] **T03** — Per-type TxForms label margin 4 → 6（8 forms, 43 occurrences）
- [x] **T04** — Keyboard shortcuts help dialog（Ctrl+/）— 列 13 個現有快捷鍵
- [x] **T05** — Tooltip 系統一致化（template 加 CornerRadius、Xs font、BorderLight、MaxWidth=320）
- [x] **T06** — Snackbar styling audit（border lightened，其他 already polished）
- [x] **T07** — Recent commands in Command Palette（LRU 5 items，empty query 時 prepend）
- [x] **T08** — Reduced-motion preference 處理（Motion.SkeletonPulseDuration 在 system pref MenuAnimation=false 時 override 成 0）
- [x] **T09** — 既存硬碼 hex 收編（Reports warning/info banner → AppWarningSubtle/AppInfoSubtle/AppWarning/Brush.Info；Calendar overlay → Brush.ModalOverlay）

## 🟡 Medium (0.5-1 day)

- [x] **T10** — AppCard* migration（4 widgets 改用 AppCardSubtle，DashboardWidgetCard alias 刪除）
- [x] **T11** — Empty state per-page check（TradesTabPanel migrated；10 個 view 早已用 AppEmptyState）
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

✅ T01 — FIRE widget 4 metrics (FIRE 目標 / 預估自由年份 / 完成進度 / 累計投入)
✅ T02 — Skeleton 擴展到總負債 + 淨資產 mini cards
✅ T03 — TxForms label margin 4→6 (8 forms, 43 occurrences)
✅ T04 — Ctrl+/ Keyboard shortcuts help dialog + AppKbdChip style
✅ T05 — Tooltip refresh (template CornerRadius、Xs font、BorderLight、MaxWidth 320)
✅ T06 — Snackbar border lightened
✅ T07 — Command Palette LRU recent commands (5 items)
✅ T08 — Reduced-motion preference (SystemParameters.MenuAnimation gate)
✅ T09 — Hex 收編 (Reports + Calendar overlay → tokens)
✅ T10 — AppCard* migration (4 widgets → AppCardSubtle)
✅ T11 — TradesTabPanel empty state → AppEmptyState style
