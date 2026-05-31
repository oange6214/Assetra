using System.Text.RegularExpressions;
using Assetra.Core.Interfaces;

namespace Assetra.Application.Assistant;

/// <summary>
/// Phase 1 的 <see cref="IFinancialAssistant"/> 實作 — 規則式 dispatcher，
/// 比對固定模板（淨資產 / 現金餘額 / 月支出 / 海外所得 / 持股市值），
/// 直接呼叫對應 query service。完全 deterministic，無 LLM 依賴。
///
/// <para>
/// 規則庫為 zh-TW / en-US 兩套；每條規則 = (regex pattern, handler delegate)。
/// 識別失敗時回傳 <see cref="FinancialAssistantResponse.Unhandled"/>，UI 應提示
/// 使用者參考 <see cref="SuggestedQueries"/>。
/// </para>
/// </summary>
public sealed class RuleBasedFinancialAssistant : IFinancialAssistant
{
    private readonly IBalanceQueryService _balances;
    private readonly IAppSettingsService? _settings;
    private readonly ICurrencyService? _currency;

    public RuleBasedFinancialAssistant(
        IBalanceQueryService balances,
        IAppSettingsService? settings = null,
        ICurrencyService? currency = null)
    {
        _balances = balances ?? throw new ArgumentNullException(nameof(balances));
        _settings = settings;
        _currency = currency;
    }

    public IReadOnlyList<string> SuggestedQueries { get; } =
    [
        "我目前的淨資產是多少？",
        "現在所有現金帳戶的餘額？",
        "我的負債總額是多少？",
        "本月還有多少預算？",
        "海外所得有達到 AMT 申報門檻嗎？",
    ];

    public async Task<FinancialAssistantResponse> AnswerAsync(FinancialAssistantQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var text = (query.Text ?? string.Empty).Trim();
        if (text.Length == 0)
            return FinancialAssistantResponse.Unhandled(GetUnhandledHint(query.Locale));

        // Order matters: more specific patterns first.
        if (MatchAny(text, "淨資產", "net worth"))
            return await AnswerNetWorthAsync(ct).ConfigureAwait(false);
        if (MatchAny(text, "現金", "cash balance", "balances?"))
            return await AnswerCashBalancesAsync(ct).ConfigureAwait(false);
        if (MatchAny(text, "負債", "liabilit"))
            return await AnswerLiabilitiesAsync(ct).ConfigureAwait(false);
        if (MatchAny(text, "AMT", "海外所得", "overseas income"))
            return AnswerAmtThreshold();

        return FinancialAssistantResponse.Unhandled(GetUnhandledHint(query.Locale));
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    private async Task<FinancialAssistantResponse> AnswerNetWorthAsync(CancellationToken ct)
    {
        var cash = await _balances.GetAllCashBalancesAsync().ConfigureAwait(false);
        var liab = await _balances.GetAllLiabilitySnapshotsAsync().ConfigureAwait(false);
        var baseCcy = ResolveBaseCurrency();
        // Sum the .Amount only (currency-conversion across accounts is out of scope
        // for this rule-based MVP — we acknowledge the limitation in the answer text).
        var totalCash = cash.Values.Sum(m => m.Amount);
        var totalLiab = liab.Values.Sum(s => s.Balance.Amount);
        var net = totalCash - totalLiab;
        var formatted = Format(net, baseCcy);
        return new FinancialAssistantResponse(
            $"目前現金合計約 {Format(totalCash, baseCcy)}、負債約 {Format(totalLiab, baseCcy)}，" +
            $"淨資產約 {formatted}（不含投資部位市值；跨幣別未換算）。",
            Source: nameof(IBalanceQueryService));
    }

    private async Task<FinancialAssistantResponse> AnswerCashBalancesAsync(CancellationToken ct)
    {
        var cash = await _balances.GetAllCashBalancesAsync().ConfigureAwait(false);
        if (cash.Count == 0)
            return new FinancialAssistantResponse("尚無任何現金帳戶交易紀錄。", Source: nameof(IBalanceQueryService));
        var lines = cash
            .OrderByDescending(kv => kv.Value.Amount)
            .Take(8)
            .Select(kv => $"  • {kv.Key.ToString()[..8]}…：{kv.Value.Amount:N0} {kv.Value.Currency}");
        return new FinancialAssistantResponse(
            $"共 {cash.Count} 個現金帳戶（按餘額降冪，最多顯示 8 筆）：\n" + string.Join('\n', lines),
            Source: nameof(IBalanceQueryService));
    }

    private async Task<FinancialAssistantResponse> AnswerLiabilitiesAsync(CancellationToken ct)
    {
        var liab = await _balances.GetAllLiabilitySnapshotsAsync().ConfigureAwait(false);
        if (liab.Count == 0)
            return new FinancialAssistantResponse("目前無未清負債紀錄。", Source: nameof(IBalanceQueryService));
        var totalCurr = liab.Values.Sum(s => s.Balance.Amount);
        var totalOrig = liab.Values.Sum(s => s.OriginalAmount.Amount);
        var paid = totalOrig > 0 ? Math.Round((totalOrig - totalCurr) / totalOrig * 100m, 1) : 0m;
        return new FinancialAssistantResponse(
            $"共 {liab.Count} 筆負債、未清餘額合計 {totalCurr:N0}、原始借款 {totalOrig:N0}（已償還 {paid}%）。",
            Source: nameof(IBalanceQueryService));
    }

    private FinancialAssistantResponse AnswerAmtThreshold()
    {
        var s = _settings?.Current;
        if (s is null)
            return new FinancialAssistantResponse(
                "AMT 申報門檻為個人海外所得 100 萬 NTD；實際是否觸發請至「報表 → 年度稅務」頁查看本年度海外所得合計。",
                Source: "TaxCalculationService");
        return new FinancialAssistantResponse(
            "AMT 個人海外所得申報門檻為 100 萬 NTD（歷年皆同）。免稅額/稅率依年度：2014–2023 為 670 萬 / 20%、" +
            "2024 起調整為 750 萬 / 20%。實際本年度海外所得合計與應補繳金額請至「報表 → 年度稅務」頁查看。",
            Source: "TaxCalculationService");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool MatchAny(string input, params string[] patterns)
    {
        foreach (var p in patterns)
            if (Regex.IsMatch(input, p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return true;
        return false;
    }

    private string ResolveBaseCurrency()
    {
        var b = _settings?.Current.BaseCurrency;
        return string.IsNullOrWhiteSpace(b) ? "TWD" : b!;
    }

    private string Format(decimal amount, string ccy) =>
        _currency is null ? $"{amount:N0} {ccy}" : _currency.FormatAmount(amount);

    private static string GetUnhandledHint(string locale) =>
        locale.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? "I don't know how to answer that yet — try one of the suggested queries."
            : "目前還無法回答這類問題，請參考下方建議查詢範例。";
}
