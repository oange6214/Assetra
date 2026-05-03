# Wpf.Ui 全面遷移設計文件

**日期：** 2026-05-03  
**專案：** Assetra.WPF  
**Wpf.Ui 版本：** 4.2.0

---

## 背景

專案已部分採用 Wpf.Ui，共 569 個 `ui:` 控件實例（Button 211、TextBox 177、SymbolIcon 98、TabView 系列 48 等），覆蓋 81.5% 的 XAML 檔案。本次遷移目標為消除剩餘的標準 WPF 控件，並移除手工維護的自訂 Button 樣式，統一改用 Wpf.Ui 原生機制。

---

## 遷移範圍（共 ~208 個控件實例）

| 標準控件 | 數量 | 處理方式 |
|---------|------|---------|
| `Border` | 53 | 語意容器 → `ui:Card`；純佈局 → 保留 |
| `DataGrid` | 43 | 保留標準，GlobalStyles.xaml 補統一樣式 + 局部 override |
| `ComboBox` | 33 | → `ui:ComboBox` |
| `Button` | 26 | → `ui:Button` |
| `ScrollViewer` | 14 | ControlsDictionary 自動樣式化，僅補 `ui:` 前綴當有特殊需求 |
| `Expander` | 10 | 有語意 → `ui:CardExpander`；純折疊 → 保留標準 + ControlsDictionary |
| `RadioButton` | 10 | → `ui:RadioButton` |
| `ListBox` | 10 | → `ui:ListBox` |
| `CheckBox` | 4 | → `ui:CheckBox` |

**不在範圍：** LiveCharts 圖表元件、`Themes/`、`Languages/` 資源字典、已使用 `ui:` 的控件。

---

## Button 自訂樣式遷移

### 移除的樣式（GlobalStyles.xaml）

| 舊樣式 key | 用量 | 替換方式 |
|-----------|------|---------|
| `BtnPrimary` | 2 | `ui:Button Appearance="Primary"` |
| `BtnGhost` | 4 | `ui:Button Appearance="Secondary"` |
| `BtnIcon` | 20 | `ui:Button Appearance="Transparent"` |
| `BtnIconToggle` | 1 | 標準 `ToggleButton`（ControlsDictionary 套樣式，Wpf.Ui 4.x 無原生 `ui:ToggleButton`） |

### 保留的樣式

- `BtnPeriod` — 期間選擇器，含 DataTrigger 特殊語意，無對應 Wpf.Ui 等效
- `DataGridDeleteBtn` — DataGrid 行內刪除按鈕，危險色語意

---

## Border → ui:Card 判斷標準

換成 `ui:Card` 的條件（需全部符合）：
1. 包含多個子控件（非單一子元素）
2. 有明確的 `Padding`（代表內容區塊，非純分隔）
3. 非純佈局用途（非 clip mask、非純分隔線）

保留 `Border` 的情況：
- 單純圓角 clip
- 分隔線 / 裝飾性邊框
- 動畫 / 特效容器

---

## DataGrid 樣式策略

在 `GlobalStyles.xaml` 新增全域 DataGrid 樣式：

```xml
<Style TargetType="{x:Type DataGrid}"
       BasedOn="{StaticResource {x:Type DataGrid}}">
    <Setter Property="RowHeight" Value="40"/>
    <Setter Property="BorderThickness" Value="0"/>
    <Setter Property="GridLinesVisibility" Value="Horizontal"/>
    <!-- 使用 Wpf.Ui 顏色 token -->
</Style>
```

各模組的 DataGrid 可視需要加局部 `Style` override，不強制統一所有細節。

---

## 模組批次計畫（方案 B）

### Batch 1 — Portfolio（核心主流程）
`Features/Portfolio/`（含 Controls/、SubViewModels/、TxForms/）

### Batch 2 — Import + FinancialOverview + Hubs
`Features/Import/`、`Features/FinancialOverview/`、`Features/Hubs/`

### Batch 3 — Reports + Categories + Recurring
`Features/Reports/`、`Features/Categories/`、`Features/Recurring/`

### Batch 4 — Goals + Fire + MonteCarlo
`Features/Goals/`、`Features/Fire/`、`Features/MonteCarlo/`

### Batch 5 — 剩餘模組 + GlobalStyles 收尾
`Features/RealEstate/`、`Features/PhysicalAsset/`、`Features/Insurance/`、`Features/Alerts/`、`Features/Reconciliation/`

**GlobalStyles.xaml 收尾（Batch 5 最後執行）：**
- 新增 DataGrid 全域統一樣式
- 移除 `BtnPrimary`、`BtnGhost`、`BtnIcon`、`BtnIconToggle` Style 定義
  （各 XAML 檔的 `Style` 引用替換隨所屬批次進行，GlobalStyles 定義在所有引用都清除後才刪除）

---

## 每批驗收標準

1. `dotnet build Assetra.slnx` — 零錯誤、零 XAML parse warning
2. `dotnet test Assetra.Tests/Assetra.Tests.csproj` — 現有測試全數通過
3. 改動的 XAML 檔案無遺漏 `xmlns:ui` 宣告

---

## 禁止事項

- 不修改 `Themes/`、`Languages/` 資源字典
- 不改動 ViewModel 邏輯（純 XAML 層變更）
- 不引入新的第三方依賴
- 不移除 `BtnPeriod`、`DataGridDeleteBtn` 樣式
