# Assetra — Claude Code 指引

## 快速導覽

| 目的 | 路徑 |
|------|------|
| DI 組合根 | `Assetra.WPF/Infrastructure/AppBootstrapper.cs` |
| 領域模型 | `Assetra.Core/Models/` |
| 服務介面 | `Assetra.Core/Interfaces/` |
| HTTP 客戶端 | `Assetra.Infrastructure/Http/` |
| 資料庫 | `Assetra.Infrastructure/Persistence/` |
| 主視窗 | `Assetra.WPF/Shell/MainWindow.xaml` |
| 全域樣式 | `Assetra.WPF/Themes/GlobalStyles.xaml` |
| 語言 Key | `Assetra.WPF/Languages/zh-TW.xaml` (正體中文，主要) |

## 建置與測試

```bash
dotnet build Assetra.slnx
dotnet test Assetra.Tests/Assetra.Tests.csproj
dotnet format
```

## 架構原則

- **依賴方向：** `Core ← Infrastructure ← WPF`
- **ViewModel：** `ObservableObject` + `[RelayCommand]`
- **DI：** 集中在 `AppBootstrapper.cs`；ViewModel 一律 `AddSingleton`
- **資料庫：** SQLite WAL 於 `%APPDATA%\Assetra\assetra.db`
- **UI 文字：** 所有字串放 `Languages/*.xaml`，DataGrid 欄標題用 Header 內嵌 TextBlock

## 語言系統

兩個語言檔（zh-TW + en-US），新增 UI 文字時兩檔都要加。

## 不做的事

- 不在此專案引用 `Stockra.*` 命名空間——Assetra 是獨立 fork
- 不加 AI／新聞／選股／自訂策略等模組（屬於 Stockra）
- 不在 WPF 執行緒以外直接操作 `ObservableCollection`
