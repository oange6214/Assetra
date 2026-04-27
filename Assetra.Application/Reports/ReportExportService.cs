using System.Globalization;
using System.Text;
using Assetra.Core.Interfaces.Reports;
using Assetra.Core.Models.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Assetra.Application.Reports;

/// <summary>
/// 把三張報表匯出為 PDF（QuestPDF）或 CSV（自寫；無外部依賴）。
/// PDF 共用 <see cref="StatementDocument"/> 樣式：標題 / 期間 / 多 section / 總計。
/// </summary>
public sealed class ReportExportService : IReportExportService
{
    private static int _licenseConfigured;

    public ReportExportService()
    {
        if (Interlocked.Exchange(ref _licenseConfigured, 1) == 0)
            QuestPDF.Settings.License = LicenseType.Community;
    }

    public Task ExportAsync(IncomeStatement statement, ExportFormat format, string filePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(statement);
        return format switch
        {
            ExportFormat.Pdf => Task.Run(() =>
                new StatementDocument(
                    title: "Income Statement",
                    subtitle: $"{statement.Period.Start:yyyy-MM-dd} – {statement.Period.End:yyyy-MM-dd}",
                    sections: [statement.Income, statement.Expense],
                    grandTotalLabel: "Net",
                    grandTotal: statement.Net).GeneratePdf(filePath), ct),
            ExportFormat.Csv => WriteCsvAsync(filePath, [statement.Income, statement.Expense], grandTotal: ("Net", statement.Net), ct),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    public Task ExportAsync(BalanceSheet statement, ExportFormat format, string filePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(statement);
        return format switch
        {
            ExportFormat.Pdf => Task.Run(() =>
                new StatementDocument(
                    title: "Balance Sheet",
                    subtitle: $"As of {statement.AsOf:yyyy-MM-dd}",
                    sections: [statement.Assets, statement.Liabilities],
                    grandTotalLabel: "Net Worth",
                    grandTotal: statement.NetWorth).GeneratePdf(filePath), ct),
            ExportFormat.Csv => WriteCsvAsync(filePath, [statement.Assets, statement.Liabilities], ("Net Worth", statement.NetWorth), ct),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    public Task ExportAsync(CashFlowStatement statement, ExportFormat format, string filePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(statement);
        return format switch
        {
            ExportFormat.Pdf => Task.Run(() =>
                new StatementDocument(
                    title: "Cash Flow Statement",
                    subtitle:
                        $"{statement.Period.Start:yyyy-MM-dd} – {statement.Period.End:yyyy-MM-dd}  |  " +
                        $"Opening {statement.OpeningCash:N2}  →  Closing {statement.ClosingCash:N2}",
                    sections: [statement.Operating, statement.Investing, statement.Financing],
                    grandTotalLabel: "Net Change",
                    grandTotal: statement.NetChange).GeneratePdf(filePath), ct),
            ExportFormat.Csv => WriteCsvAsync(
                filePath,
                [statement.Operating, statement.Investing, statement.Financing],
                ("Net Change", statement.NetChange),
                ct,
                extraRows: [
                    ("Opening Cash", statement.OpeningCash),
                    ("Closing Cash", statement.ClosingCash),
                ]),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    private static async Task WriteCsvAsync(
        string filePath,
        IReadOnlyList<StatementSection> sections,
        (string Label, decimal Amount) grandTotal,
        CancellationToken ct,
        IReadOnlyList<(string Label, decimal Amount)>? extraRows = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Section,Label,Group,Amount");
        foreach (var s in sections)
        {
            foreach (var r in s.Rows)
            {
                sb.Append(Csv(s.Title)).Append(',')
                  .Append(Csv(r.Label)).Append(',')
                  .Append(Csv(r.Group ?? string.Empty)).Append(',')
                  .Append(r.Amount.ToString("F2", CultureInfo.InvariantCulture))
                  .Append('\n');
            }
            sb.Append(Csv(s.Title)).Append(',')
              .Append("Subtotal,,")
              .Append(s.Total.ToString("F2", CultureInfo.InvariantCulture))
              .Append('\n');
        }

        if (extraRows is not null)
            foreach (var (label, amt) in extraRows)
                sb.Append(",,").Append(Csv(label)).Append(',')
                  .Append(amt.ToString("F2", CultureInfo.InvariantCulture)).Append('\n');

        sb.Append(",,").Append(Csv(grandTotal.Label)).Append(',')
          .Append(grandTotal.Amount.ToString("F2", CultureInfo.InvariantCulture)).Append('\n');

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8, ct).ConfigureAwait(false);
    }

    private static string Csv(string s)
    {
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}

internal sealed class StatementDocument : IDocument
{
    private readonly string _title;
    private readonly string _subtitle;
    private readonly IReadOnlyList<StatementSection> _sections;
    private readonly string _grandTotalLabel;
    private readonly decimal _grandTotal;

    public StatementDocument(
        string title,
        string subtitle,
        IReadOnlyList<StatementSection> sections,
        string grandTotalLabel,
        decimal grandTotal)
    {
        _title = title;
        _subtitle = subtitle;
        _sections = sections;
        _grandTotalLabel = grandTotalLabel;
        _grandTotal = grandTotal;
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Margin(36);
            page.Size(PageSizes.A4);
            page.DefaultTextStyle(t => t.FontSize(10));

            page.Header().Column(col =>
            {
                col.Item().Text(_title).FontSize(18).SemiBold();
                col.Item().Text(_subtitle).FontSize(10).FontColor(Colors.Grey.Darken1);
            });

            page.Content().PaddingTop(12).Column(col =>
            {
                foreach (var s in _sections)
                {
                    col.Item().PaddingTop(8).Text(s.Title).SemiBold().FontSize(12);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cd =>
                        {
                            cd.RelativeColumn(2);
                            cd.RelativeColumn(2);
                            cd.RelativeColumn(1);
                        });
                        table.Header(h =>
                        {
                            h.Cell().Text("Group").SemiBold();
                            h.Cell().Text("Label").SemiBold();
                            h.Cell().AlignRight().Text("Amount").SemiBold();
                        });
                        foreach (var r in s.Rows)
                        {
                            table.Cell().Text(r.Group ?? string.Empty);
                            table.Cell().Text(r.Label);
                            table.Cell().AlignRight().Text(r.Amount.ToString("N2", CultureInfo.InvariantCulture));
                        }
                        table.Cell().ColumnSpan(2).Text("Subtotal").SemiBold();
                        table.Cell().AlignRight().Text(s.Total.ToString("N2", CultureInfo.InvariantCulture)).SemiBold();
                    });
                }

                col.Item().PaddingTop(12).BorderTop(1).BorderColor(Colors.Grey.Darken2)
                   .PaddingTop(6).Row(row =>
                {
                    row.RelativeItem().Text(_grandTotalLabel).SemiBold().FontSize(12);
                    row.ConstantItem(140).AlignRight().Text(
                        _grandTotal.ToString("N2", CultureInfo.InvariantCulture)).SemiBold().FontSize(12);
                });
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("Page ");
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        });
    }
}
