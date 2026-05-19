using Assetra.Application.MarketData;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Application.MarketData;

public class TradingCalendarServiceTests
{
    private readonly TradingCalendarService _calendar = new();

    [Fact]
    public void GetTradingDayKind_BlackFridayIsUsHalfSession()
    {
        Assert.Equal(
            TradingDayKind.HalfSession,
            _calendar.GetTradingDayKind("NASDAQ", new DateOnly(2026, 11, 27)));
    }

    [Fact]
    public void ShouldRefreshQuotes_BlackFridayStopsAfterEarlyClose()
    {
        Assert.True(_calendar.ShouldRefreshQuotes("NASDAQ", DateTimeOffset.Parse("2026-11-27T17:59:00Z")));
        Assert.False(_calendar.ShouldRefreshQuotes("NASDAQ", DateTimeOffset.Parse("2026-11-27T18:01:00Z")));
    }

    [Fact]
    public void ShouldRefreshQuotes_ChristmasEveStopsAfterEarlyClose()
    {
        Assert.True(_calendar.ShouldRefreshQuotes("NYSE", DateTimeOffset.Parse("2026-12-24T17:59:00Z")));
        Assert.False(_calendar.ShouldRefreshQuotes("NYSE", DateTimeOffset.Parse("2026-12-24T18:01:00Z")));
    }

    [Fact]
    public void ShouldRefreshQuotes_JulyThirdHalfDayStopsAfterEarlyClose()
    {
        Assert.Equal(
            TradingDayKind.HalfSession,
            _calendar.GetTradingDayKind("NYSEARCA", new DateOnly(2025, 7, 3)));
        Assert.True(_calendar.ShouldRefreshQuotes("NYSEARCA", DateTimeOffset.Parse("2025-07-03T16:59:00Z")));
        Assert.False(_calendar.ShouldRefreshQuotes("NYSEARCA", DateTimeOffset.Parse("2025-07-03T17:01:00Z")));
    }

    [Theory]
    [InlineData("2026-03-09T13:30:00Z", true)]
    [InlineData("2026-03-09T20:01:00Z", false)]
    [InlineData("2026-11-02T14:30:00Z", true)]
    [InlineData("2026-11-02T21:01:00Z", false)]
    [InlineData("2027-03-15T13:30:00Z", true)]
    [InlineData("2027-11-08T14:30:00Z", true)]
    public void ShouldRefreshQuotes_HandlesUsDstTransitions(string utc, bool expected)
    {
        Assert.Equal(expected, _calendar.ShouldRefreshQuotes("NASDAQ", DateTimeOffset.Parse(utc)));
    }

    [Fact]
    public void GetTradingDayKind_GoodFridayIsUsHoliday()
    {
        Assert.Equal(
            TradingDayKind.Holiday,
            _calendar.GetTradingDayKind("NASDAQ", new DateOnly(2026, 4, 3)));
    }

    [Fact]
    public void GetTradingDayKind_TaiwanMakeupSaturdayRemainsWeekend()
    {
        Assert.Equal(
            TradingDayKind.Weekend,
            _calendar.GetTradingDayKind("TWSE", new DateOnly(2026, 2, 21)));
        Assert.False(_calendar.ShouldRefreshQuotes("TPEX", DateTimeOffset.Parse("2026-02-21T02:00:00Z")));
    }
}
