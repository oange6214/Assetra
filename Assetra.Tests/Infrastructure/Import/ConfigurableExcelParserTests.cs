using System.IO;
using Assetra.Infrastructure.Import;
using ClosedXML.Excel;
using Xunit;

namespace Assetra.Tests.Infrastructure.Import;

public class ConfigurableExcelParserTests
{
    [Fact]
    public async Task Generic_ParsesXlsxWithSignedAmount()
    {
        using var ms = BuildWorkbook(headers: new[] { "Date", "Amount", "Counterparty", "Memo" }, rows: new[]
        {
            new object[] { new DateTime(2026, 4, 26), -250m, "Starbucks", "Latte" },
            new object[] { new DateTime(2026, 4, 27), 1500m, "Salary", "April" },
        });

        var parser = new ConfigurableExcelParser(ExcelParserConfigs.Generic);
        var rows = await parser.ParseAsync(ms);

        Assert.Equal(2, rows.Count);
        Assert.Equal(-250m, rows[0].Amount);
        Assert.Equal("Starbucks", rows[0].Counterparty);
        Assert.Equal(1500m, rows[1].Amount);
    }

    [Fact]
    public async Task CathayBank_ParsesDebitCreditLayout()
    {
        using var ms = BuildWorkbook(headers: new[] { "日期", "提出", "存入", "摘要", "備註" }, rows: new[]
        {
            new object?[] { new DateTime(2026, 4, 26), 250m, null, "星巴克", "拿鐵" },
            new object?[] { new DateTime(2026, 4, 27), null, 1500m, "薪資", "四月" },
        });

        var parser = new ConfigurableExcelParser(ExcelParserConfigs.CathayUnitedBank);
        var rows = await parser.ParseAsync(ms);

        Assert.Equal(2, rows.Count);
        Assert.Equal(-250m, rows[0].Amount);
        Assert.Equal(1500m, rows[1].Amount);
    }

    private static MemoryStream BuildWorkbook(string[] headers, object?[][] rows)
    {
        using var wb = new XLWorkbook();
        var sheet = wb.AddWorksheet("Sheet1");
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }
        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                var v = rows[r][c];
                if (v is null) continue;
                var cell = sheet.Cell(r + 2, c + 1);
                switch (v)
                {
                    case DateTime dt: cell.Value = dt; cell.Style.DateFormat.Format = "yyyy/MM/dd"; break;
                    case decimal d: cell.Value = d; break;
                    case string s: cell.Value = s; break;
                    default: cell.Value = v.ToString(); break;
                }
            }
        }

        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }
}
