# Assetra

個人財務桌面應用程式（WPF, .NET 10）。

- 追蹤現金、信用卡、負債、投資組合的整體財務狀況
- 盤中即時股票報價（TWSE / TPEX / Fugle / CoinGecko）
- 資產配置分析（Treemap + 再平衡）
- 交易記錄 + 配息追蹤 + 價格警示
- **月結報告**：收入 / 支出 / 淨額 / 儲蓄率，含與上月差額、超支與即將到期清單
- **淨資產趨勢**：30 / 90 / 180 / 365 / All 預設區間 + 自訂日期範圍
- **財務目標（MVP）**：目標金額、進度、期限追蹤
- **預算 / 週期性交易**：分類預算、訂閱與固定支出自動產生

目前里程碑版本：`v0.6.0`

## 架構

專案分為四層：

- `Assetra.Core` — 領域模型與介面
- `Assetra.Application` — workflow / query / summary services
- `Assetra.Infrastructure` — SQLite 持久層、HTTP 客戶端、報價排程
- `Assetra.WPF` — MVVM UI（依 bounded context 切 `Features/`）

主要 bounded contexts：`Portfolio` / `Budgeting` / `Recurring` / `Goals` / `Reports` / `Alerts` / `Loans` / `Platform`，詳見 [Bounded Contexts](docs/architecture/Bounded-Contexts.md)。

相關文件：

- [Docs Index](docs/INDEX.md)
- [Architecture](docs/architecture/Architecture.md)
- [Portfolio Module Map](docs/architecture/Portfolio-Module-Map.md)
- [Technical Architecture Blueprint](docs/architecture/Technical-Architecture-Blueprint.md)
- [Bounded Contexts](docs/architecture/Bounded-Contexts.md)
- [Feature Blueprint and Roadmap](docs/planning/Assetra-Feature-Blueprint-and-Roadmap.md)
- [Implementation Roadmap](docs/planning/Implementation-Roadmap.md)
- [Next Sprint (v0.6.0)](docs/planning/Next-Sprint-v0.6.0.md)
- [Changelog](docs/releases/CHANGELOG.md)
- [Fugle API Key Setup](docs/guides/Fugle-API-Key-Setup.md)

## 建置

```bash
dotnet build Assetra.slnx
dotnet test Assetra.Tests/Assetra.Tests.csproj
dotnet run --project Assetra.WPF
```

資料庫位於 `%APPDATA%\Assetra\assetra.db`（SQLite WAL）。
