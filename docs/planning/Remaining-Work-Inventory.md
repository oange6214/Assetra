# 剩餘工作盤點 — Remaining Work Inventory

**Snapshot date:** 2026-05-09
**Baseline:** master @ 144 commits ahead of origin · 1201 / 1201 tests passing
**Source-of-truth docs:**
- `docs/planning/v1.0-GA-Sprint-Plan.md`
- `docs/planning/Deferred-Roadmap.md`
- `docs/planning/Settings-Schema-Migration-v1.md`

本檔為單頁總覽，便於下次 sprint planning 一眼看完。實際執行細節請翻對應 plan doc。

---

## 🚀 v1.0 GA Sprint（最優先，4–6 週）

詳細：`v1.0-GA-Sprint-Plan.md`

| Phase | 內容 | 估時 |
|---|---|---|
| 1. 硬化 | 回歸測試掃描 + e2e + Settings schema migration v0→v1 | 1 週 |
| 2. 發布 | MSIX 打包 + EV code signing + Velopack 自動更新 | 1–2 週 |
| 3. 文件 | 使用手冊 + 截圖 + release notes + 隱私頁 | 1 週 |
| 4. 上線 | rc1 → closed beta → v1.0.0 | 1 週 |

**Acceptance gate：** 1200+ tests、Critical/High 0 bug、signed MSIX 在 Win10/11 乾淨 VM 可裝、auto-update rc1→1.0 通過。

---

## 📦 v1.1（GA 後 point release）

| 項目 | 估時 | 備註 |
|---|---|---|
| LlmApiKey 移到 OS 憑證庫 | 4–6h | Win Credential Manager / macOS Keychain；保留讀舊寫新的回退 |
| AI insight 去重持久化 | 2–3h | `last_shown_at` 進 SQLite |
| AI 對話歷史跨 session 搜尋 | 3–4h | `WHERE user_text LIKE @q OR assistant_text LIKE @q` |
| 審計還原後 Portfolio reload hook | 2h | 掛 `TransactionCompleted` |
| Sync 衝突 UI 改善 | 1–2 週 | side-by-side diff page |
| i18n 全面 audit | 3–4h | Roslyn analyzer 抓硬編字串 |
| ~~NavRail 縮起捲軸 hotfix~~ | ~~5 min~~ | **已 obsolete — NavRail D 方案已重做（見 v0.27.x changelog），縮起模式只剩 4 個群組圖示 + 2 個底部，無捲軸**|

**總計：** ~3–4 週

---

## 🛠️ v1.2（下一個 minor）

| 項目 | 估時 | 備註 |
|---|---|---|
| PortfolioViewModel + Reload.cs 拆分（B2/B3）| 10–14h | 最大殘留 god-object |
| AI Phase 4 — action automation | 2–3 週 | 需 security review |
| AI Phase 3.7 — OpenAI function-calling 正規協定 | 8–12h | 取代 3.5 的 splice-all |
| AI 月度摘要 email/push | 1 週 | 接 SMTP / FCM |
| Sync 升級 Argon2id | 1 週 | 含舊裝置 migration |

**總計：** ~5–7 週

---

## 🌐 v2.0（D 桶，平台級擴張）

| 項目 | 估時 |
|---|---|
| PWA frontend（REST 拆出 + Svelte/React SPA） | 3–4 週 |
| Mobile（iOS / Android，MAUI 或 native） | 4–6 週 |
| Push notifications（APNs + FCM） | 2 週 |
| 多資產擴張（CoinGecko crypto / 私募基金 / 選擇權鏈） | 4–6 週 |
| ~~NavRail 重設計：可摺疊群組 + flyout~~ | ~~2–4 天~~ | **✅ 已完成（提早動工）** — 4 群組（Overview / Assets / Cashflow / Planning）+ 2 底部固定，展開模式可折疊 group 標題，縮起模式 group icon 點擊彈 Popup flyout 顯示子項。資料驅動 (`NavGroupVm` / `NavLeafVm`)、與 `ILocalizationService` 整合自動換語言、徽章 (Recurring/Alerts) 沿用。|

**總計：** 13–18 週（建議拆多個 release）

---

## 🔬 Always-on 品質工作（無版本綁定）

| 項目 | 估時 |
|---|---|
| BenchmarkDotNet 效能 profiling + 1 萬筆 fixture | 1 週 |
| 測試覆蓋率量測（coverlet → codecov，80% gate）| 3–5 天 |
| WCAG 2.1 無障礙 audit | 1 週 |
| Reports 低解析度（1280×720）佈局 pass | 2–3h |
| Tx 完整 event-sourcing audit（目前只記刪除）| 8–12h |

**插縫策略：** 每個 v1.x sprint 結束後挑 1–2 項當 cooldown 工作。

---

## 全局結論

- 沒有「小事」剩了 — 全部是 sprint 級工作。
- 下次動工兩條路：
  1. **進入 v1.0 GA Sprint** — 4–6 週實際發布
  2. **挑 v1.1 batch** — 加總 1.5–2 週、可分批吃
- 中型重構（H1 god-object、M6-B 收編、AI Phase 1–3.5、L3 解耦、audit log）已全數完成 merge，不再列入此盤點。

## 動工時的處理

挑出某項時：
1. 從本檔對應 bucket 找該項
2. 移到 `Implementation-Roadmap.md` 寫完整 plan doc 處理
3. 從本檔 + `Deferred-Roadmap.md` 同步刪除
4. 更新本檔 header 的 Snapshot date
