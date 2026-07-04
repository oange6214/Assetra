# Assetra

> 整合現金、投資、負債到退休規劃的個人財務桌面應用。WPF · .NET 10。

Assetra 是離線優先（本機 SQLite）、可多裝置端到端加密同步的個人財務桌面軟體。以台灣使用者為主 —— 台股與複委託、台灣銀行即期匯率、股利與海外所得報稅 —— 同時支援美股與加密貨幣。

## 功能

**資產與投資**

- 現金、信用卡、負債、投資組合的整體淨資產與趨勢追蹤
- 台股／美股／加密貨幣盤中即時報價（TWSE、TPEX、Fugle、Yahoo、CoinGecko）
- 交易記錄、配息追蹤、跨幣別（複委託）成本與現金流
- 資產配置 Treemap ＋ 再平衡建議、集中度分析
- 績效比較：與大盤或自訂標的對比，支援盤中分時曲線

**收支與規劃**

- 月結報告：收入／支出／淨額／儲蓄率，含月增減、超支與即將到期提醒
- 分類預算、訂閱與週期性交易自動產生
- 財務目標：里程碑、定期投入、進度追蹤
- FIRE 計算機、Monte Carlo 退休提領與長期資產路徑模擬

**報表與資料**

- 損益表、資產負債表、現金流量表（PDF／CSV 匯出）
- 投資績效分析（XIRR、TWR／MWR、損益歸因）與風險指標（年化波動率、最大回撤、Sharpe）
- 稅務彙整：股利／海外所得追蹤、報稅匯出
- 對帳單匯入（CSV／Excel／PDF／OCR）＋自動分類、匯入歷史與回滾、對帳工作台
- 多元資產：不動產、保險保單、退休專戶、實物資產

**系統**

- 端到端 AES-GCM 加密的多裝置雲端同步
- AI 財務助理：規則式 ＋ LLM，可對話、grounded 查詢、洞察排程
- Fluent 風格原生 Design System（設計 tokens、共用元件）

> 最新發佈 `v0.33.0`。版本由 git tag 透過 MinVer 推算 —— 完整版本歷史見 [Releases](https://github.com/oange6214/Assetra/releases) 與 [Changelog](docs/releases/CHANGELOG.md)。

## 架構

四層，依賴方向 `Core ← Application ← Infrastructure ← WPF`：

| 層 | 職責 |
|----|------|
| `Assetra.Core` | 領域模型與介面 |
| `Assetra.Application` | workflow／query／summary 服務 |
| `Assetra.Infrastructure` | SQLite 持久層、HTTP 客戶端、報價排程 |
| `Assetra.WPF` | MVVM UI，依 bounded context 切 `Features/` |

主要 bounded contexts：Portfolio、Budgeting、Recurring、Goals、Analysis、Reporting、Importing、Reconciliation、Tax、Sync、FX、Platform。

## 建置

```bash
dotnet build Assetra.slnx
dotnet test Assetra.Tests/Assetra.Tests.csproj
dotnet run --project Assetra.WPF
```

資料庫位於 `%APPDATA%\Assetra\assetra.db`（SQLite WAL）。

## 文件

- [Docs Index](docs/INDEX.md) · [Architecture](docs/architecture/Architecture.md) · [Bounded Contexts](docs/architecture/Bounded-Contexts.md)
- [Portfolio Module Map](docs/architecture/Portfolio-Module-Map.md) · [Technical Architecture Blueprint](docs/architecture/Technical-Architecture-Blueprint.md)
- [Feature Blueprint & Roadmap](docs/planning/Assetra-Feature-Blueprint-and-Roadmap.md) · [Implementation Roadmap](docs/planning/Implementation-Roadmap.md)
- [Changelog](docs/releases/CHANGELOG.md) · [Fugle API Key 設定](docs/guides/Fugle-API-Key-Setup.md) · [雲端同步設定](docs/guides/Cloud-Sync-Setup.md)
