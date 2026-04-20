using Assetra.Infrastructure.Search;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class StockSearchServiceTests
{
    private const string TwseCsv = """
        type,code,name,ISIN,start,market,group,CFI
        股票,2330,台積電,TW0002330008,1994/09/05,上市,半導體,ESVUFR
        股票,2317,鴻海,TW0002317005,1991/06/11,上市,電腦周邊,ESVUFR
        認購權證,12345,XX認購,TW0012345678,2024/01/01,上市,,RWSCPE
        """;

    private const string TpexCsv = """
        type,code,name,ISIN,start,market,group,CFI
        股票,6547,高端疫苗,TW0006547004,2020/07/20,上櫃,生技醫療,ESVUFR
        """;

    [Fact]
    public void Search_BySymbol_ReturnsMatch()
    {
        var svc = new StockSearchService(TwseCsv, TpexCsv);
        var results = svc.Search("2330");
        Assert.Single(results);
        Assert.Equal("台積電", results[0].Name);
        Assert.Equal("TWSE", results[0].Exchange);
    }

    [Fact]
    public void Search_ByName_ReturnsMatch()
    {
        var svc = new StockSearchService(TwseCsv, TpexCsv);
        var results = svc.Search("鴻海");
        Assert.Single(results);
        Assert.Equal("2317", results[0].Symbol);
    }

    [Fact]
    public void Search_ExcludesNonStock()
    {
        var svc = new StockSearchService(TwseCsv, TpexCsv);
        var results = svc.Search("12345");
        Assert.Empty(results);
    }

    [Fact]
    public void Search_IncludesTpexStocks()
    {
        var svc = new StockSearchService(TwseCsv, TpexCsv);
        var results = svc.Search("6547");
        Assert.Single(results);
        Assert.Equal("TPEX", results[0].Exchange);
    }

    [Fact]
    public void GetExchange_ReturnsCorrectExchange()
    {
        var svc = new StockSearchService(TwseCsv, TpexCsv);
        Assert.Equal("TWSE", svc.GetExchange("2330"));
        Assert.Equal("TPEX", svc.GetExchange("6547"));
        Assert.Null(svc.GetExchange("9999"));
    }

    [Fact]
    public void Search_ReturnsEmpty_ForWhitespaceQuery()
    {
        var svc = new StockSearchService(TwseCsv, TpexCsv);
        Assert.Empty(svc.Search(""));
        Assert.Empty(svc.Search("   "));
    }

    [Fact]
    public void GetAll_ReturnsOnlyEquities_WithCorrectExchanges()
    {
        var twseCsv = "type,code,name,ISIN,start,market,group,CFI\n" +
                      "股票,2330,台積電,TW0002330008,1994/09/05,上市,半導體,ESVUFR\n" +
                      "股票,2317,鴻海,TW0002317003,1991/06/11,上市,電子,ESVUFR\n";
        var tpexCsv = "type,code,name,ISIN,start,market,group,CFI\n" +
                      "股票,6505,台塑化,TW0006505008,2003/12/09,上櫃,石化,ESVUFR\n";
        var svc = new StockSearchService(twseCsv, tpexCsv);

        var all = svc.GetAll();

        Assert.Equal(3, all.Count);
        Assert.Contains(all, r => r.Symbol == "2330" && r.Exchange == "TWSE");
        Assert.Contains(all, r => r.Symbol == "6505" && r.Exchange == "TPEX");
    }
}
