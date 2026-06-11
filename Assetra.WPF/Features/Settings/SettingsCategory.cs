namespace Assetra.WPF.Features.Settings;

/// <summary>
/// 設定頁左側分類導覽的九大分類。每個分類對應 <c>Categories/</c> 底下一個
/// <c>UserControl</c> 子視圖；右側內容區由 <see cref="SettingsViewModel.SelectedCategory"/>
/// 透過 DataTrigger 切換。新增設定 = 新增一個 UserControl + 一個 enum 值 + 一條 nav 項目。
/// </summary>
public enum SettingsCategory
{
    /// <summary>外觀：主題、漲跌色、UI 縮放。</summary>
    Appearance,

    /// <summary>語言與區域：語言、主要 / 基準幣別、即時匯率。</summary>
    LanguageRegion,

    /// <summary>資料來源：台股 / 歷史資料來源、API 金鑰、美股代號目錄、FX 歷史、OCR。</summary>
    DataSources,

    /// <summary>儀表板：自訂對標、資產類焦點卡顯示。</summary>
    Dashboard,

    /// <summary>交易：預設手續費折扣。</summary>
    Trading,

    /// <summary>報表與稅務：最低稅負制（AMT）、個人稅務檔案。</summary>
    ReportsTax,

    /// <summary>AI 助手：LLM provider / key / model / endpoint。</summary>
    Assistant,

    /// <summary>雲端同步：同步設定、密語、衝突解決。</summary>
    Sync,

    /// <summary>資料與維護：資料目錄、版本資訊、重建快照歷史。</summary>
    DataMaintenance,
}
