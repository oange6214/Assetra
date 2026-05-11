using System.Text.Json;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Core;

/// <summary>
/// AppSettings 是 positional record，整個由 System.Text.Json 序列化到本地檔。
/// 加新欄位時要驗：
/// - 舊 settings JSON 缺少新欄位 → 反序列化用 record ctor default 值（不 throw）
/// - 新值寫回 → JSON 正確 round-trip
///
/// 這組測試針對 v0.30+ 新加的 DismissedAssistantInsights，但同模式適用任何
/// 未來新增欄位。
/// </summary>
public sealed class AppSettingsSerializationTests
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    [Fact]
    public void OldSettingsJson_WithoutNewField_LoadsWithDefaultNull()
    {
        // 模擬舊 JSON：只有 v0.20.x 既有欄位，沒有 DismissedAssistantInsights
        var oldJson = """{ "Language": "zh-TW", "UiScale": 1.0 }""";

        var settings = JsonSerializer.Deserialize<AppSettings>(oldJson, Options);

        Assert.NotNull(settings);
        Assert.Equal("zh-TW", settings!.Language);
        Assert.Null(settings.DismissedAssistantInsights);   // default value 套用
    }

    [Fact]
    public void DismissedAssistantInsights_RoundTripsThroughJson()
    {
        var original = new AppSettings(
            DismissedAssistantInsights: new Dictionary<string, DateTime>
            {
                { "Budget|餐飲超支", new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc) },
                { "Recurring|信用卡帳單到期", new DateTime(2026, 5, 10, 14, 30, 0, DateTimeKind.Utc) },
            });

        var json = JsonSerializer.Serialize(original, Options);
        var roundTripped = JsonSerializer.Deserialize<AppSettings>(json, Options);

        Assert.NotNull(roundTripped);
        Assert.NotNull(roundTripped!.DismissedAssistantInsights);
        Assert.Equal(2, roundTripped.DismissedAssistantInsights!.Count);
        Assert.True(roundTripped.DismissedAssistantInsights.ContainsKey("Budget|餐飲超支"));
        Assert.Equal(
            new DateTime(2026, 5, 10, 14, 30, 0, DateTimeKind.Utc),
            roundTripped.DismissedAssistantInsights["Recurring|信用卡帳單到期"]);
    }

    [Fact]
    public void EmptyDismissedMap_SerializesAndDeserializesCleanly()
    {
        var original = new AppSettings(
            DismissedAssistantInsights: new Dictionary<string, DateTime>());

        var json = JsonSerializer.Serialize(original, Options);
        var roundTripped = JsonSerializer.Deserialize<AppSettings>(json, Options);

        Assert.NotNull(roundTripped?.DismissedAssistantInsights);
        Assert.Empty(roundTripped!.DismissedAssistantInsights!);
    }
}
