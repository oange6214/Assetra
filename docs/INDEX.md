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
- [Assetra Feature Blueprint and Roadmap](planning/Assetra-Feature-Blueprint-and-Roadmap.md)
  - 產品功能藍圖與 P0 / P1 / P2 優先級。
- [Implementation Roadmap](planning/Implementation-Roadmap.md)
  - 以 phase 拆解的實作任務清單。

## Guides
- [Fugle API Key Setup](guides/Fugle-API-Key-Setup.md)
  - Fugle API key 申請與設定方式。

## Reviews
- [Responsive UI Review](reviews/Responsive-UI-Review.md)
  - 響應式 UI 整修結果與剩餘待改善項目。

## Releases
- [Changelog](releases/CHANGELOG.md)
  - 版本里程碑與主要變更摘要。

## Archive
- `archive/superpowers/`
  - 歷史設計與規劃草稿，保留作為參考，不作為目前正式架構文件。
