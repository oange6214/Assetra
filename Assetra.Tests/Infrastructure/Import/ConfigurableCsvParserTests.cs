using System.IO;
using System.Text;
using Assetra.Core.Models.Import;
using Assetra.Infrastructure.Import;
using Xunit;

namespace Assetra.Tests.Infrastructure.Import;

public class ConfigurableCsvParserTests
{
    [Fact]
    public async Task Generic_ParsesUtf8WithSignedAmount()
    {
        const string csv = "Date,Amount,Counterparty,Memo\n2026-04-26,-250.00,Starbucks,Latte\n2026-04-27,1500,Salary,April\n";
        var parser = new ConfigurableCsvParser(CsvParserConfigs.Generic);

        var rows = await ParseAsync(parser, csv, Encoding.UTF8);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new DateOnly(2026, 4, 26), rows[0].Date);
        Assert.Equal(-250m, rows[0].Amount);
        Assert.Equal("Starbucks", rows[0].Counterparty);
        Assert.Equal(1500m, rows[1].Amount);
    }

    [Fact]
    public async Task Cathay_ParsesBig5DebitCreditLayout()
    {
        const string csv = "日期,提出,存入,摘要,備註\n2026/04/26,250,,星巴克,拿鐵\n2026/04/27,,1500,薪資,四月\n";
        var parser = new ConfigurableCsvParser(CsvParserConfigs.CathayUnitedBank);

        var rows = await ParseAsync(parser, csv, Encoding.GetEncoding("big5"));

        Assert.Equal(2, rows.Count);
        Assert.Equal(-250m, rows[0].Amount); // 提出 → negative
        Assert.Equal("星巴克", rows[0].Counterparty);
        Assert.Equal(1500m, rows[1].Amount); // 存入 → positive
    }

    [Fact]
    public async Task Yuanta_ParsesBrokerWithSymbolAndQuantity()
    {
        const string csv = "成交日期,股票代號,成交股數,成交金額,買賣別,備註\n2026/04/26,2330,1000,910000,買進,\n";
        var parser = new ConfigurableCsvParser(CsvParserConfigs.YuantaSecurities);

        var rows = await ParseAsync(parser, csv, Encoding.GetEncoding("big5"));

        Assert.Single(rows);
        Assert.Equal("2330", rows[0].Symbol);
        Assert.Equal(1000m, rows[0].Quantity);
        Assert.Equal(910000m, rows[0].Amount);
        Assert.Equal("買進", rows[0].Counterparty);
    }

    [Fact]
    public async Task SkipsRows_WhenDateUnparseable()
    {
        const string csv = "Date,Amount\nN/A,100\n2026-04-26,250\n";
        var parser = new ConfigurableCsvParser(CsvParserConfigs.Generic);

        var rows = await ParseAsync(parser, csv, Encoding.UTF8);

        Assert.Single(rows);
        Assert.Equal(250m, rows[0].Amount);
    }

    [Fact]
    public async Task HandlesAmountWithCommasAndCurrencyPrefix()
    {
        const string csv = "Date,Amount\n2026-04-26,\"NT$1,234.56\"\n";
        var parser = new ConfigurableCsvParser(CsvParserConfigs.Generic);

        var rows = await ParseAsync(parser, csv, Encoding.UTF8);

        Assert.Single(rows);
        Assert.Equal(1234.56m, rows[0].Amount);
    }

    [Fact]
    public void Format_ReportsConfigFormat()
    {
        var parser = new ConfigurableCsvParser(CsvParserConfigs.EsunBank);
        Assert.Equal(ImportFormat.EsunBank, parser.Format);
        Assert.Equal(ImportFileType.Csv, parser.FileType);
    }

    private static async Task<IReadOnlyList<ImportPreviewRow>> ParseAsync(
        ConfigurableCsvParser parser, string csv, Encoding encoding)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var ms = new MemoryStream(encoding.GetBytes(csv));
        return await parser.ParseAsync(ms, CancellationToken.None);
    }
}
