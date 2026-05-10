using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Tax;

/// <summary>
/// 從 <c>Assetra.Core/Resources/TaxYearProfiles.json</c> 內嵌資源載入歷年稅制 profile。
/// 啟動時一次解析全部年度，後續 <see cref="Get"/> 為 O(log n) 字典查找。
/// 缺漏年度回退至最接近的已知年度（2027 → 2026；2018 → 2020）並標記 IsExtrapolated。
/// </summary>
public sealed class EmbeddedTaxProfileProvider : ITaxProfileProvider
{
    private const string ResourceName = "Assetra.Core.Resources.TaxYearProfiles.json";

    private static readonly Lazy<IReadOnlyDictionary<int, TaxYearProfile>> _cache =
        new(LoadFromEmbeddedResource, isThreadSafe: true);

    public TaxYearProfile Get(int year)
    {
        var map = _cache.Value;
        if (map.TryGetValue(year, out var exact))
            return exact;

        // Fallback：找最接近的已知年（先選之前年；若都沒有則選之後）
        var keys = map.Keys.OrderBy(k => k).ToList();
        if (keys.Count == 0)
            throw new InvalidOperationException("TaxYearProfiles.json 內無可用年度資料。");

        int nearest;
        var earlier = keys.LastOrDefault(k => k < year);
        var later = keys.FirstOrDefault(k => k > year);
        if (earlier == default && later == default)
            nearest = keys[0];
        else if (earlier == default)
            nearest = later;
        else if (later == default)
            nearest = earlier;
        else
            nearest = year - earlier <= later - year ? earlier : later;

        var src = map[nearest];
        return src with { Year = year, IsExtrapolated = true };
    }

    public IReadOnlyList<int> SupportedYears => _cache.Value.Keys.OrderBy(k => k).ToList();

    private static IReadOnlyDictionary<int, TaxYearProfile> LoadFromEmbeddedResource()
    {
        // Core.dll holds the embedded resource; locate via the Assetra.Core assembly.
        var asm = typeof(TaxYearProfile).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"嵌入資源 {ResourceName} 未找到。請確認 .csproj 內有 <EmbeddedResource>。");

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        var doc = JsonSerializer.Deserialize<TaxYearProfilesDocument>(stream, options)
            ?? throw new InvalidOperationException("TaxYearProfiles.json 反序列化失敗。");

        var dict = new Dictionary<int, TaxYearProfile>();
        foreach (var entry in doc.Profiles ?? [])
        {
            var brackets = (entry.IncomeTaxBrackets ?? [])
                .Select(b => new TaxBracket(b.UpTo, b.Rate, b.Subtract))
                .ToList();

            dict[entry.Year] = new TaxYearProfile(
                Year: entry.Year,
                IncomeTaxBrackets: brackets,
                PersonalExemption: entry.PersonalExemption,
                StandardDeductionSingle: entry.StandardDeductionSingle,
                StandardDeductionMarried: entry.StandardDeductionMarried,
                SalarySpecialDeduction: entry.SalarySpecialDeduction,
                SavingsInvestmentDeductionCap: entry.SavingsInvestmentDeductionCap,
                LongCareDeduction: entry.LongCareDeduction,
                PreschoolDeduction: entry.PreschoolDeduction,
                DisabilityDeduction: entry.DisabilityDeduction,
                EducationDeduction: entry.EducationDeduction,
                RentalDeduction: entry.RentalDeduction,
                DividendCreditRate: entry.DividendCreditRate,
                DividendCreditCap: entry.DividendCreditCap,
                DividendSeparateRate: entry.DividendSeparateRate,
                AmtExemption: entry.AmtExemption,
                AmtRate: entry.AmtRate,
                AmtOverseasThreshold: entry.AmtOverseasThreshold,
                AmtInsuranceDeduction: entry.AmtInsuranceDeduction,
                IsExtrapolated: false);
        }
        return dict;
    }

    // ── JSON DTO（內部，僅供反序列化使用）────────────────────────────────

    private sealed class TaxYearProfilesDocument
    {
        [JsonPropertyName("profiles")] public List<TaxYearProfileDto>? Profiles { get; set; }
    }

    private sealed class TaxYearProfileDto
    {
        [JsonPropertyName("year")] public int Year { get; set; }
        [JsonPropertyName("incomeTaxBrackets")] public List<BracketDto>? IncomeTaxBrackets { get; set; }
        [JsonPropertyName("personalExemption")] public decimal PersonalExemption { get; set; }
        [JsonPropertyName("standardDeductionSingle")] public decimal StandardDeductionSingle { get; set; }
        [JsonPropertyName("standardDeductionMarried")] public decimal StandardDeductionMarried { get; set; }
        [JsonPropertyName("salarySpecialDeduction")] public decimal SalarySpecialDeduction { get; set; }
        [JsonPropertyName("savingsInvestmentDeductionCap")] public decimal SavingsInvestmentDeductionCap { get; set; }
        [JsonPropertyName("longCareDeduction")] public decimal LongCareDeduction { get; set; }
        [JsonPropertyName("preschoolDeduction")] public decimal PreschoolDeduction { get; set; }
        [JsonPropertyName("disabilityDeduction")] public decimal DisabilityDeduction { get; set; }
        [JsonPropertyName("educationDeduction")] public decimal EducationDeduction { get; set; }
        [JsonPropertyName("rentalDeduction")] public decimal RentalDeduction { get; set; }
        [JsonPropertyName("dividendCreditRate")] public decimal DividendCreditRate { get; set; }
        [JsonPropertyName("dividendCreditCap")] public decimal DividendCreditCap { get; set; }
        [JsonPropertyName("dividendSeparateRate")] public decimal DividendSeparateRate { get; set; }
        [JsonPropertyName("amtExemption")] public decimal AmtExemption { get; set; }
        [JsonPropertyName("amtRate")] public decimal AmtRate { get; set; }
        [JsonPropertyName("amtOverseasThreshold")] public decimal AmtOverseasThreshold { get; set; }
        [JsonPropertyName("amtInsuranceDeduction")] public decimal AmtInsuranceDeduction { get; set; }
    }

    private sealed class BracketDto
    {
        [JsonPropertyName("upTo")] public decimal? UpTo { get; set; }
        [JsonPropertyName("rate")] public decimal Rate { get; set; }
        [JsonPropertyName("subtract")] public decimal Subtract { get; set; }
    }
}
