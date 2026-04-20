namespace Assetra.Core.Interfaces;

/// <summary>
/// 提供貨幣格式化與換算服務。
/// 所有金額來源皆為台幣（TWD）；切換至其他貨幣時除以對應匯率。
/// </summary>
public interface ICurrencyService
{
    /// <summary>目前顯示貨幣代碼，例如 "TWD"、"USD"、"JPY"、"EUR"、"HKD"。</summary>
    string Currency { get; }

    /// <summary>1 USD = N TWD（向後相容，同 ExchangeRates["USD"]）。</summary>
    decimal UsdTwdRate { get; }

    /// <summary>支援的貨幣代碼清單（固定順序：TWD、USD、JPY、EUR、HKD）。</summary>
    IReadOnlyList<string> SupportedCurrencies { get; }

    /// <summary>
    /// 各幣別兌台幣匯率（1 單位外幣 = N 台幣）。
    /// 若尚未從 Frankfurter 取得，回傳硬碼預設值。
    /// </summary>
    IReadOnlyDictionary<string, decimal> ExchangeRates { get; }

    /// <summary>貨幣或匯率變更時觸發（UI 需監聽此事件以重新整理顯示）。</summary>
    event Action? CurrencyChanged;

    /// <summary>格式化整數金額（N0），例如 NT$1,234,567 或 $38,580 或 ¥4,210,000。</summary>
    string FormatAmount(decimal twdValue);

    /// <summary>格式化單價（N2），例如 NT$185.50 或 $5.80。</summary>
    string FormatPrice(decimal twdValue);

    /// <summary>格式化帶正負號的損益（+/-#,0），例如 +NT$1,000 或 -$31。</summary>
    string FormatSigned(decimal twdValue);

    /// <summary>套用新顯示貨幣並觸發 CurrencyChanged（匯率本身透過 RefreshRatesAsync 更新）。</summary>
    Task ApplyAsync(string currency);

    /// <summary>
    /// 從 Frankfurter 取得最新匯率並持久化至 AppSettings.ExchangeRates。
    /// 失敗時靜默（使用硬碼預設或上次快取的匯率），不拋出例外。
    /// </summary>
    Task RefreshRatesAsync(CancellationToken ct = default);
}
