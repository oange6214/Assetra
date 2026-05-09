# Documentation Index

這裡整理 Assetra 的架構、規劃、指南、檢視與版本文件，作為專案文件的正式入口頁。

## Recommended Reading Order

如果你的目標是「先理解現在架構，再開始開發尚未實現的功能」，建議用這個順序讀：

1. [Architecture](architecture/Architecture.md)
2. [Portfolio Module Map](architecture/Portfolio-Module-Map.md)
3. [Technical Architecture Blueprint](architecture/Technical-Architecture-Blueprint.md)
4. [Bounded Contexts](architecture/Bounded-Contexts.md)
5. [Assetra Feature Blueprint and Roadmap](planning/Assetra-Feature-Blueprint-and-Roadmap.md)
6. [Implementation Roadmap](planning/Implementation-Roadmap.md)

### How to Use These Docs

- 想知道「現在怎麼做」
  - 先看 `Architecture` 和 `Portfolio Module Map`
- 想知道「未來應該怎麼做」
  - 看 `Technical Architecture Blueprint` 和 `Bounded Contexts`
- 想知道「下一步先做什麼」
  - 看 `Feature Blueprint and Roadmap` 和 `Implementation Roadmap`

### Before Starting a New Feature

開始新功能前，先回答這四個問題：

1. 這個功能屬於哪個 context？
2. 它是 UI state、query、workflow，還是 pure calculation？
3. 它應該先落在哪一層？
4. 專案裡有沒有相近的 service / workflow / query 可延用？

## UI Work Workflow

要做 UI 重構或 DesignSystem 變更時，照下列順序使用文件：

1. **計畫（why & what）** → [Assetra Fluent + Carbon UI Plan](planning/Assetra-Fluent-Carbon-UI-Plan.md)
   - 設計決策（Fluent 主、Carbon 輔）、Phase 0–8、頁面模式、Acceptance Rules。
2. **範圍檢查（避免重做）** → [Native UI Baseline Migration Completion](reviews/Assetra-WPF-Native-UI-Migration-Completion.md)
   - 確認 native baseline 已完成哪些事；這不是最終 UI 品質門檻，後續仍以 Fluent + Carbon plan 為準。
3. **資源歸屬（資源在哪）** → [DesignSystem README](../Assetra.WPF/DesignSystem/README.md)
   - Fluent-first / Carbon-assisted 的資源歸屬、canonical 檔案、命名規則、migration rules。
4. **實作參考（最常查）** → [DesignSystem USAGE](../Assetra.WPF/DesignSystem/USAGE.md)
   - 寫 XAML 時的 page、form rhythm、dialog、empty state、data grid、report、analysis、shared product pattern。
5. **驗收（每批變更後）** → [UI Release Gate](reviews/Assetra-WPF-UI-Release-Gate.md)
   - 自動化 / 手動 / control state / pattern state / blockers 五道關卡。
6. **收尾（release note）** → [Changelog](releases/CHANGELOG.md)
   - 每完成一個 phase 或重要 batch 後補上版本紀錄。

> 一句話總結：**計畫看 Plan，scope 看 Migration Completion，實作看 README + USAGE，驗收看 Release Gate，收尾寫 Changelog。**

## Architecture
- [Architecture](architecture/Architecture.md)
  - 目前的分層原則與依賴方向。
- [Portfolio Module Map](architecture/Portfolio-Module-Map.md)
  - `Portfolio` 主模組的服務與責任分布。
- [Technical Architecture Blueprint](architecture/Technical-Architecture-Blueprint.md)
  - 偏中長期的技術架構藍圖。
- [Bounded Contexts](architecture/Bounded-Contexts.md)
  - 未來可拆分的業務 context 邊界。

## Planning
- [Assetra US Market Data Plan](planning/Assetra-US-Market-Data-Plan.md)
  - 台股與美股共存的 market data 架構計畫，包含 symbol directory、quote provider、quota、cache、trading calendar、FX valuation 與導入 phase。
- [Assetra Fluent + Carbon UI Plan](planning/Assetra-Fluent-Carbon-UI-Plan.md)
  - Assetra WPF 以 Fluent Design 為主、Carbon 為輔的 UI 設計決策、頁面模式與分階段重構計畫。
- [Assetra Feature Blueprint and Roadmap](planning/Assetra-Feature-Blueprint-and-Roadmap.md)
  - 產品功能藍圖與 P0 / P1 / P2 優先級。
- [Implementation Roadmap](planning/Implementation-Roadmap.md)
  - 以 phase 拆解的實作任務清單。
- [Roadmap v0.14 to v1.0](planning/Roadmap-v0.14-to-v1.0.md)
  - v0.14–v1.0 各 sprint 詳細 task breakdown。Phase 4
    （多元資產 + 情境模擬）已於 **v0.22.0** 一次 ship。

### Phase 4 已 ship（v0.22.0）

多元資產（不動產 / 保險 / 退休 / 實物資產）+ 情境模擬
（FIRE / Monte Carlo）合併進 **v0.22.0** release。要了解
context 與架構決策仍可讀：

1. [Bounded Contexts §13–14](architecture/Bounded-Contexts.md) — MultiAsset / Simulation context 定義
2. [Technical Architecture Blueprint §十三–十四](architecture/Technical-Architecture-Blueprint.md) — folder 結構、EntityVersion 規範、BalanceSheet 擴充
3. [Changelog v0.22.0](releases/CHANGELOG.md) — 完整變更摘要

## Guides
- [Fugle API Key Setup](guides/Fugle-API-Key-Setup.md)
  - Fugle API key 申請與設定方式。
- [Cloud Sync Setup](guides/Cloud-Sync-Setup.md)
  - 雲端同步啟用、加密模型、Conflict 解決與 troubleshooting（v0.20.0–v0.21.0）。

## Reviews
- [Assetra WPF Native UI Baseline Migration Completion](reviews/Assetra-WPF-Native-UI-Migration-Completion.md)
  - 原生 WPF DesignSystem baseline 收斂完成狀態、已知缺口、gallery 截圖與驗證命令。
- [Assetra WPF UI Release Gate](reviews/Assetra-WPF-UI-Release-Gate.md)
  - DesignSystem 或主要 XAML 變更進 release 前的自動、手動與失敗訊號驗收 gate。

## Releases
- [Changelog](releases/CHANGELOG.md)
  - 版本里程碑與主要變更摘要。

## Archive
- `archive/superpowers/`
  - 歷史設計與規劃草稿，保留作為參考，不作為目前正式架構文件。
- `archive/sprints/`
  - 已完成的 sprint plan（例如 v0.6.0），保留作為紀錄。
