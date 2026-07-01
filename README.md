# Assetra

個人財務桌面應用程式（WPF, .NET 10）。

- 追蹤現金、信用卡、負債、投資組合的整體財務狀況
- 盤中即時股票報價（TWSE / TPEX / Fugle / CoinGecko）
- 資產配置分析（Treemap + 再平衡）
- 交易記錄 + 配息追蹤 + 價格警示
- **月結報告**：收入 / 支出 / 淨額 / 儲蓄率，含與上月差額、超支與即將到期清單
- **淨資產趨勢**：30 / 90 / 180 / 365 / All 預設區間 + 自訂日期範圍，含事件標註與類別堆疊圖（v0.17）
- **財務目標（v0.16）**：完整子系統含 milestone、funding rule、GoalPlanningService、GoalProgressQueryService
- **預算 / 週期性交易**：分類預算、訂閱與固定支出自動產生
- **對帳單匯入**：CSV / Excel / PDF / OCR 全鏈路（v0.7–v0.19），含 AutoCategorizationRule、匯入歷史 + rollback、對帳工作台
- **正式報表與匯出（v0.11）**：損益表、資產負債表、現金流量表，支援 PDF / CSV 匯出
- **投資績效分析（v0.12）**：XIRR、TWR / MWR、benchmark 對比、損益歸因
- **風險分析（v0.13）**：年化波動率、最大回撤、Sharpe Ratio、集中度分析（HHI + Top-N）
- **外幣 / 跨市場基礎（v0.14–v0.15）**：Currency VO、FxRate、MultiCurrencyValuationService、StockExchangeRegistry、Yahoo history routing；美股即時報價與 symbol directory 已落地（v0.29+）
- **稅務模組（v0.18）**：TaxSummary、股利 / 海外所得追蹤、報稅匯出
- **雲端同步（v0.20–v0.21）**：端到端 AES-GCM 加密、8 entity round-trip、LastWriteWins + manual conflict drain
- **多元資產（v0.22）**：不動產、保險保單、退休專戶、實物資產，接入資產負債表與同步模型
- **情境模擬（v0.22）**：FIRE 計算機、Monte Carlo、退休提領與長期資產路徑試算
- **AI 財務助理（v0.27）**：規則式 + LLM fallback、grounded tools、洞察排程、對話歷史與 Markdown 匯出
- **Native DesignSystem（v0.28）**：Fluent-first / Carbon-assisted UI 規範、tokens、shared controls、release gate 與文件化 migration path
- **投資資產頁 Google 式重構（v0.29–v0.32）**：持股表重整（去冗餘、欄內視覺層級、可逐欄排序）、入口正名分層、投資組合改為對話框、資金帳戶分類、外幣頁尾彙總修正；即時／歷史匯率改台灣銀行即期買入、設定頁改分類導覽
- **績效比較盤中重構（v0.33）**：1D／5D 改吃盤中分時、跨市場 Google 式真實時間軸、Google 式 hover 讀數（十字準星＋線上圓點＋日期時間／現值／同期 %）；圖表與對標清單合併成一張卡
- **跨幣別交易完善（v0.33）**：賣出對齊買入（成交總額＋進階收合）、新增標的代號自動完成（台股＋美股）、「依帳戶明細」用自動匯率反推單價、同幣別交易 FX 持久化防呆；資金帳戶交易清單直接顯示資產名稱

最新發佈版本為 `v0.33.0`（2026-07）。版本由 git tag 透過 MinVer 推算；下一階段重點是 v1.0 GA hardening 與美股 market data 深化。

> 註：功能敘事以 `master` 為準；已正式發佈的版本號見 [GitHub Releases](https://github.com/oange6214/Assetra/releases) 與 [Changelog](docs/releases/CHANGELOG.md)。

## 架構

專案分為四層：

- `Assetra.Core` — 領域模型與介面
- `Assetra.Application` — workflow / query / summary services
- `Assetra.Infrastructure` — SQLite 持久層、HTTP 客戶端、報價排程
- `Assetra.WPF` — MVVM UI（依 bounded context 切 `Features/`）

主要 bounded contexts：`Portfolio` / `Budgeting` / `Recurring` / `Goals` / `Analysis` / `Reporting` / `Importing` / `Reconciliation` / `Tax` / `Sync` / `FX` / `Platform`，詳見 [Bounded Contexts](docs/architecture/Bounded-Contexts.md)。

相關文件：

- [Docs Index](docs/INDEX.md)
- [Architecture](docs/architecture/Architecture.md)
- [Portfolio Module Map](docs/architecture/Portfolio-Module-Map.md)
- [Technical Architecture Blueprint](docs/architecture/Technical-Architecture-Blueprint.md)
- [Bounded Contexts](docs/architecture/Bounded-Contexts.md)
- [Feature Blueprint and Roadmap](docs/planning/Assetra-Feature-Blueprint-and-Roadmap.md)
- [Implementation Roadmap](docs/planning/Implementation-Roadmap.md)
- [Changelog](docs/releases/CHANGELOG.md)
- [Fugle API Key Setup](docs/guides/Fugle-API-Key-Setup.md)
- [Cloud Sync Setup](docs/guides/Cloud-Sync-Setup.md)

## 建置

```bash
dotnet build Assetra.slnx
dotnet test Assetra.Tests/Assetra.Tests.csproj
dotnet run --project Assetra.WPF
```

資料庫位於 `%APPDATA%\Assetra\assetra.db`（SQLite WAL）。
