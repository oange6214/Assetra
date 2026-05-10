using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

/// <summary>
/// 提供指定年度的台灣稅制參數快照（綜所稅級距、AMT 免稅額等）。
/// 實作可從內建 JSON、本地覆寫檔、或遠端 API 載入；caller 應將回傳的
/// <see cref="TaxYearProfile"/> 視為唯讀。
/// </summary>
public interface ITaxProfileProvider
{
    /// <summary>
    /// 取得指定 <paramref name="year"/> 的稅制 profile。
    /// 若該年度未內建，回傳最接近年度的 profile 並標記
    /// <see cref="TaxYearProfile.IsExtrapolated"/> = true。
    /// </summary>
    TaxYearProfile Get(int year);

    /// <summary>所有內建年度（升冪），供 UI 顯示「資料涵蓋 2020–2026」用。</summary>
    IReadOnlyList<int> SupportedYears { get; }
}
