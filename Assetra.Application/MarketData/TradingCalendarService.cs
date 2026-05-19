using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.MarketData;

public sealed class TradingCalendarService : ITradingCalendarService
{
    private static readonly HashSet<string> UsExchanges = new(StringComparer.OrdinalIgnoreCase)
    {
        "NASDAQ",
        "NYSE",
        "NYSEARCA",
        "AMEX",
        "BATS",
        "IEX",
    };

    private static readonly HashSet<string> TaiwanExchanges = new(StringComparer.OrdinalIgnoreCase)
    {
        "TWSE",
        "TPEX",
    };

    public TradingDayKind GetTradingDayKind(string exchange, DateOnly localDate)
    {
        var normalized = EquitySymbolNormalizer.NormalizeExchange(exchange);
        if (IsWeekend(localDate))
            return TradingDayKind.Weekend;

        if (UsExchanges.Contains(normalized))
            return GetUsTradingDayKind(localDate);

        if (TaiwanExchanges.Contains(normalized))
            return TradingDayKind.FullSession;

        return TradingDayKind.FullSession;
    }

    public bool ShouldRefreshQuotes(string exchange, DateTimeOffset utcNow)
    {
        var normalized = EquitySymbolNormalizer.NormalizeExchange(exchange);
        var localNow = ToExchangeLocalTime(normalized, utcNow);
        var localDate = DateOnly.FromDateTime(localNow.DateTime);
        var kind = GetTradingDayKind(normalized, localDate);

        if (kind is TradingDayKind.Weekend or TradingDayKind.Holiday)
            return false;

        if (!UsExchanges.Contains(normalized))
            return true;

        var open = new TimeOnly(9, 30);
        var close = kind == TradingDayKind.HalfSession
            ? new TimeOnly(13, 0)
            : new TimeOnly(16, 0);
        var localTime = TimeOnly.FromDateTime(localNow.DateTime);
        return localTime >= open && localTime <= close;
    }

    private static TradingDayKind GetUsTradingDayKind(DateOnly date)
    {
        if (IsUsFullHoliday(date))
            return TradingDayKind.Holiday;

        if (IsUsHalfDay(date))
            return TradingDayKind.HalfSession;

        return TradingDayKind.FullSession;
    }

    private static bool IsUsFullHoliday(DateOnly date)
    {
        var year = date.Year;
        return date == ObservedFixedHoliday(year, 1, 1) ||
               date == ObservedFixedHoliday(year + 1, 1, 1) ||
               date == NthWeekday(year, 1, DayOfWeek.Monday, 3) ||
               date == NthWeekday(year, 2, DayOfWeek.Monday, 3) ||
               date == GoodFriday(year) ||
               date == LastWeekday(year, 5, DayOfWeek.Monday) ||
               date == ObservedFixedHoliday(year, 6, 19) ||
               date == ObservedFixedHoliday(year, 7, 4) ||
               date == NthWeekday(year, 9, DayOfWeek.Monday, 1) ||
               date == NthWeekday(year, 11, DayOfWeek.Thursday, 4) ||
               date == ObservedFixedHoliday(year, 12, 25);
    }

    private static bool IsUsHalfDay(DateOnly date)
    {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;

        var thanksgiving = NthWeekday(date.Year, 11, DayOfWeek.Thursday, 4);
        if (date == thanksgiving.AddDays(1))
            return true;

        if (date.Month == 7 && date.Day == 3)
            return true;

        return date.Month == 12 && date.Day == 24;
    }

    private static DateOnly ObservedFixedHoliday(int year, int month, int day)
    {
        var actual = new DateOnly(year, month, day);
        return actual.DayOfWeek switch
        {
            DayOfWeek.Saturday => actual.AddDays(-1),
            DayOfWeek.Sunday => actual.AddDays(1),
            _ => actual,
        };
    }

    private static DateOnly NthWeekday(int year, int month, DayOfWeek weekday, int n)
    {
        var date = new DateOnly(year, month, 1);
        while (date.DayOfWeek != weekday)
            date = date.AddDays(1);

        return date.AddDays(7 * (n - 1));
    }

    private static DateOnly LastWeekday(int year, int month, DayOfWeek weekday)
    {
        var date = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        while (date.DayOfWeek != weekday)
            date = date.AddDays(-1);

        return date;
    }

    private static DateOnly GoodFriday(int year) => EasterSunday(year).AddDays(-2);

    private static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }

    private static bool IsWeekend(DateOnly date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static DateTimeOffset ToExchangeLocalTime(string exchange, DateTimeOffset utcNow)
    {
        var timeZone = UsExchanges.Contains(exchange)
            ? FindTimeZone("America/New_York", "Eastern Standard Time")
            : TaiwanExchanges.Contains(exchange)
                ? FindTimeZone("Asia/Taipei", "Taipei Standard Time")
                : TimeZoneInfo.Utc;

        return TimeZoneInfo.ConvertTime(utcNow, timeZone);
    }

    private static TimeZoneInfo FindTimeZone(string ianaId, string windowsId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
        }
    }
}
