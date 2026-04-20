using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Assetra.Core.Models;

namespace Assetra.WPF.Infrastructure.Chart;

/// <summary>
/// C# ↔ JS 通訊橋接器。
/// 由 <c>ChartHostControl</c> 在 WebView2 就緒後建立，並傳遞給 ViewModel。
/// </summary>
public sealed class ChartBridge : IDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // 主題常數（對應 Dark.xaml / Light.xaml）
    private static readonly object DarkTheme = new
    {
        background = "#1C1C1C",
        surface = "#282828",
        border = "#3D3D3D",
        textPrimary = "#F3F3F3",
        textSecondary = "#9E9E9E",
        up = "#0CBB8A",
        down = "#F24B4B",
        accent = "#0078D4",
    };

    private static readonly object LightTheme = new
    {
        background = "#F3F3F3",
        surface = "#FFFFFF",
        border = "#D9D9D9",
        textPrimary = "#1A1A1A",
        textSecondary = "#616161",
        up = "#0D7A5B",
        down = "#C42B1C",
        accent = "#0078D4",
    };

    private readonly CoreWebView2 _core;
    private bool _disposed;

    // JS → C# 事件
    /// <summary>JS 端圖表完成初始化並送出 READY 訊號。</summary>
    public event EventHandler? ReadyReceived;

    /// <summary>準星移動；payload = JSON string { time, price }。</summary>
    public event EventHandler<string>? CrosshairMoved;

    /// <summary>點擊 K 棒；payload = JSON string { time, ohlcv }。</summary>
    public event EventHandler<string>? BarClicked;

    public ChartBridge(CoreWebView2 core)
    {
        _core = core;
        _core.WebMessageReceived += OnWebMessageReceived;
    }

    // C# → JS

    /// <summary>推送完整圖表資料及指標開關狀態。</summary>
    public void SetData(ChartData data, IndicatorToggles toggles)
    {
        var payload = ChartSerializer.Serialize(data);
        var togglesDto = new
        {
            bollingerBands = toggles.BollingerBands,
            volumeMa = toggles.VolumeMa,
            rsi = toggles.Rsi,
            kd = toggles.Kd,
            macd = toggles.Macd,
        };
        PostCommand(new { type = "SET_DATA", payload, toggles = togglesDto });
    }

    /// <summary>切換單一指標的顯示狀態。</summary>
    public void ToggleIndicator(string name, bool visible) =>
        PostCommand(new { type = "TOGGLE", name, visible });

    /// <summary>套用深色或淺色主題。</summary>
    public void SetTheme(bool isDark) =>
        PostCommand(new { type = "SET_THEME", theme = isDark ? DarkTheme : LightTheme });

    /// <summary>新增 AI / 手動標記（水平線或 K 棒箭頭）。</summary>
    public void AddAnnotation(ChartAnnotation annotation)
    {
        var ann = annotation switch
        {
            PriceLineAnnotation pl => (object)new
            {
                kind = "priceLine",
                price = (double)pl.Price,
                color = pl.Color,
                label = pl.Label,
            },
            MarkerAnnotation m => new
            {
                kind = "marker",
                time = m.Time,
                position = m.Position,
                color = m.Color,
                text = m.Text,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(annotation)),
        };
        PostCommand(new { type = "ADD_ANNOTATION", annotation = ann });
    }

    /// <summary>清除全部標記。</summary>
    public void ClearAnnotations() =>
        PostCommand(new { type = "CLEAR_ANNOTATIONS" });

    // JS → C#

    private void OnWebMessageReceived(object? _, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            // JS 端用 postMessage(JSON.stringify(obj)) 傳的是字串。
            // WebView2 的 WebMessageAsJson 會把字串再做一次 JSON encode，
            // 導致 root element 是 String token 而非 Object。
            // 必須偵測 root kind 並 unwrap 才能正確取得物件。
            var outerJson = e.WebMessageAsJson;
            using var outer = JsonDocument.Parse(outerJson);

            string effectiveJson;
            if (outer.RootElement.ValueKind == JsonValueKind.String)
            {
                // JS 傳了 postMessage(JSON.stringify(obj))：unwrap 字串取得物件 JSON
                effectiveJson = outer.RootElement.GetString()!;
            }
            else
            {
                // JS 傳了 postMessage(obj) 直接物件（較少見）
                effectiveJson = outerJson;
            }

            using var doc = JsonDocument.Parse(effectiveJson);
            var type = doc.RootElement.GetProperty("type").GetString();
            switch (type)
            {
                case "READY":
                    ReadyReceived?.Invoke(this, EventArgs.Empty);
                    break;
                case "CROSSHAIR_MOVED":
                    CrosshairMoved?.Invoke(this, effectiveJson);
                    break;
                case "BAR_CLICKED":
                    BarClicked?.Invoke(this, effectiveJson);
                    break;
            }
        }
        catch
        {
            // ignore malformed messages
        }
    }

    // 私有工具

    private void PostCommand(object command)
    {
        var json = JsonSerializer.Serialize(command, _jsonOptions);
        _core.PostWebMessageAsJson(json);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _core.WebMessageReceived -= OnWebMessageReceived;
        _disposed = true;
    }
}
