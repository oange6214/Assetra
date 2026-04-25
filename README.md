# Assetra

資產管理桌面應用程式（WPF, .NET 10）。

- 追蹤現金、負債、投資組合的整體財務狀況
- 盤中即時股票報價（TWSE / TPEX / CoinGecko）
- 資產配置分析（Treemap + 再平衡）
- 交易記錄 + 配息追蹤
- 價格警示

目前里程碑版本：`v0.5.5`

## 架構

目前專案主要分為四層：

- `Assetra.Core`
- `Assetra.Application`
- `Assetra.Infrastructure`
- `Assetra.WPF`

`Portfolio` 主線的 mutation/query 邊界已經大致收進 application layer，`Alerts` 與 `FinancialOverview` 也已開始跟上同樣模式。

相關文件：

- [Docs Index](docs/INDEX.md)
- [Architecture](docs/architecture/Architecture.md)
- [Portfolio Module Map](docs/architecture/Portfolio-Module-Map.md)
- [Technical Architecture Blueprint](docs/architecture/Technical-Architecture-Blueprint.md)
- [Feature Blueprint and Roadmap](docs/planning/Assetra-Feature-Blueprint-and-Roadmap.md)
- [Changelog](docs/releases/CHANGELOG.md)
- [Fugle API Key Setup](docs/guides/Fugle-API-Key-Setup.md)

## 建置

```bash
dotnet build Assetra.slnx
dotnet run --project Assetra.WPF
```
