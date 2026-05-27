using System.Globalization;
using System.Windows.Data;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.WPF.Infrastructure.Converters;

/// <summary>
/// 將台幣數值格式化為目前偏好貨幣（TWD / USD）。
/// 由 AppBootstrapper 在啟動時設定 Service；
/// 使用 ConverterParameter 選擇格式：
///   "amount"  → 整數（N0），適用市值、成本、損益等
///   "price"   → 小數兩位（N2），適用單價
///   "signed"  → 帶正負號（+/-），適用損益
/// </summary>
[ValueConversion(typeof(object), typeof(string))]
public sealed class CurrencyConverter : IValueConverter
{
    /// <summary>啟動時由 AppBootstrapper 注入，所有 XAML 實例共用。</summary>
    public static ICurrencyService? Service { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Money money)
            return FormatMoney(money, parameter?.ToString());

        if (Service is null)
            return value?.ToString() ?? string.Empty;

        // 支援 decimal 與 decimal?（nullable 拆箱後仍為 decimal）
        decimal d;
        if (value is decimal dv)
            d = dv;
        else if (value is double dbl)
            d = (decimal)dbl;
        else
            return value?.ToString() ?? string.Empty;

        return parameter?.ToString() switch
        {
            "price" => Service.FormatPrice(d),
            "price-approx" => "≈ " + Service.FormatPrice(d),
            "signed" => Service.FormatSigned(d),
            "signed-approx" => "≈ " + Service.FormatSigned(d),
            "deduct" => "-" + Service.FormatAmount(Math.Abs(d)),   // 手續費/稅費（永遠負號）
            "signed-dash" => d == 0 ? "—" : Service.FormatSigned(d),   // 零顯示破折號（交易歷史）
            "amount-dash" => d == 0 ? "—" : Service.FormatAmount(d), // 零顯示破折號（原始借款）
            "price-dash" => d == 0 ? "—" : Service.FormatPrice(d),   // 零顯示破折號（單價：還款/存入等無單價的列）
            "amount-approx" => "≈ " + Service.FormatAmount(d),
            _ => Service.FormatAmount(d),   // "amount" 或無參數
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string FormatMoney(Money money, string? mode)
    {
        var amount = money.Amount;
        return mode switch
        {
            "price" => FormatNativePrice(amount, money.Currency),
            "price-approx" => "≈ " + FormatNativePrice(amount, money.Currency),
            "signed" => FormatNativeSigned(amount, money.Currency),
            "signed-approx" => "≈ " + FormatNativeSigned(amount, money.Currency),
            "deduct" => "-" + FormatNativeAmount(Math.Abs(amount), money.Currency),
            "signed-dash" => amount == 0 ? "—" : FormatNativeSigned(amount, money.Currency),
            "amount-dash" => amount == 0 ? "—" : FormatNativeAmount(amount, money.Currency),
            "price-dash" => amount == 0 ? "—" : FormatNativePrice(amount, money.Currency),
            "amount-approx" => "≈ " + FormatNativeAmount(amount, money.Currency),
            _ => FormatNativeAmount(amount, money.Currency),
        };
    }

    private static string FormatNativeAmount(decimal amount, string currency) =>
        $"{GetSymbol(currency)}{amount:N0}";

    private static string FormatNativePrice(decimal amount, string currency)
    {
        var truncated = Math.Truncate(amount * 100m) / 100m;
        return $"{GetSymbol(currency)}{truncated:N2}";
    }

    private static string FormatNativeSigned(decimal amount, string currency)
    {
        var sign = amount >= 0 ? "+" : "-";
        return $"{sign}{GetSymbol(currency)}{Math.Abs(amount):N0}";
    }

    private static string GetSymbol(string currency) => currency.Trim().ToUpperInvariant() switch
    {
        "USD" => "US$",
        "JPY" => "¥",
        "EUR" => "€",
        "HKD" => "HK$",
        _ => "NT$",
    };
}
