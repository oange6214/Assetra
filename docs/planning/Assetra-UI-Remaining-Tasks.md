# Assetra UI — 剩餘 task list

最後更新：2026-05-21 (P2.17 T01-T13 完工)

## 🛑 Project status: **CLOSED — user-driven mode**

T01-T13 全部 ship 後，使用者選擇「user-driven only」mode：
- **不再 proactive 列「未完成項目」** — 因為 UI polish 本質沒有底
- **不再自我擴張 scope** — backlog 上的項目（T20-T24）除非真實 use case
  觸發否則不主動推進
- **下次動工 trigger**：使用者開 app 看到具體痛點 → 回報 → 修

T20-T24 保留作 reference，但不再屬於「待辦」性質。

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
- [x] **T12** — Localization key audit — 1700 zh-TW / 1700 en-US keys 對齊 (補 2 個漏譯：Categories.Rule.AddTitle / Categories.Budget.AddTitle)；en-US 內無 untranslated 漢字 leak
- [x] **T13** — Search popup + Command Palette popup 兩者 border 都從 AppBorder 降到 AppBorderLight，視覺語言統一

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
✅ T12 — i18n audit + 2 missing en-US keys patched
✅ T13 — Search + Command Palette popup borders unified (AppBorderLight)

---

## P3 — Wealth Platform Design Language (mockup-driven)

依據 docs/mnt/assetra_wealth_platform_mockup.html 為設計基準 + 4 頁角色：
- Overview = Executive Dashboard
- Trend = Portfolio Analytics
- Calendar = Wealth Behavior Journal
- Allocation = Exposure Analysis

✅ P3.1 — Foundation tokens (Radius.2Xl=18 / Shadow.Card 14/36/.06 /
   AppCard* 三層統一 Padding 20)
✅ P3.2 — Overview Hero 48→50px (Font.Size.3xl bump)
✅ P3.3 — Trend KPI cards 升 AppCardSummary baseline + 數字 Md→Xl Bold Tabular
✅ P3.4 — Calendar 從整格實色 → 上半 subtle 漸層 + Strong/Strongest dot
   (Wealth Behavior Journal 行為標記風格)
✅ P3.5 — Allocation Left/Right cards 升 AppCardSummary baseline

🎉 **P3.1-P3.5 全部完成。** 「all pages look like same company」目標達成 —
所有 page-level cards 統一 Radius.2Xl + Shadow.Card + AppBorderLight。
