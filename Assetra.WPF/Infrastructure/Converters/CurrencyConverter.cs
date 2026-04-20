using System.Globalization;
using System.Windows.Data;
using Assetra.Core.Interfaces;

namespace Assetra.WPF.Infrastructure.Converters;

/// <summary>
/// 將台幣數值格式化為目前偏好貨幣（TWD / USD）。
/// 由 AppBootstrapper 在啟動時設定 Service；
/// 使用 ConverterParameter 選擇格式：
///   "amount"  → 整數（N0），適用市值、成本、損益等
///   "price"   → 小數兩位（N2），適用單價
///   "signed"  → 帶正負號（+/-），適用損益
/// </summary>
[ValueConversion(typeof(decimal), typeof(string))]
public sealed class CurrencyConverter : IValueConverter
{
    /// <summary>啟動時由 AppBootstrapper 注入，所有 XAML 實例共用。</summary>
    public static ICurrencyService? Service { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
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
            "signed" => Service.FormatSigned(d),
            "deduct" => "-" + Service.FormatAmount(Math.Abs(d)),   // 手續費/稅費（永遠負號）
            "signed-dash" => d == 0 ? "—" : Service.FormatSigned(d),   // 零顯示破折號（交易歷史）
            "amount-dash" => d == 0 ? "—" : Service.FormatAmount(d), // 零顯示破折號（原始借款）
            "price-dash" => d == 0 ? "—" : Service.FormatPrice(d),   // 零顯示破折號（單價：還款/存入等無單價的列）
            _ => Service.FormatAmount(d),   // "amount" 或無參數
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
