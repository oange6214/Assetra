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
- **外幣 / 美股（v0.14–v0.15）**：Currency VO、FxRate、MultiCurrencyValuationService、StockExchangeRegistry
- **稅務模組（v0.18）**：TaxSummary、股利 / 海外所得追蹤、報稅匯出
- **雲端同步（v0.20–v0.21）**：端到端 AES-GCM 加密、8 entity round-trip、LastWriteWins + manual conflict drain

目前開發主線里程碑：`v0.21.1`（下一個 sprint：`v0.22.0` — AI 財務助理）

> 註：這裡描述的是目前 `master` 的開發目標與功能敘事，不等同於 GitHub Releases 上已正式發佈的版本號。

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
