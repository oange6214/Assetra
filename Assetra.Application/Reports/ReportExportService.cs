using System.Globalization;
using System.Text;
using Assetra.Core.Interfaces.Reports;
using Assetra.Core.Models;
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

    public Task ExportAsync(TaxSummary summary, ExportFormat format, string filePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return format switch
        {
            ExportFormat.Pdf => Task.Run(() => new TaxSummaryDocument(summary).GeneratePdf(filePath), ct),
            ExportFormat.Csv => WriteTaxCsvAsync(filePath, summary, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    private static async Task WriteTaxCsvAsync(string filePath, TaxSummary summary, CancellationToken ct)
    {
        var sb = new StringBuilder();

        // Section 1: Dividend records
        sb.AppendLine("Section,Date,Symbol,Exchange,Country,Amount,IsOverseas");
        foreach (var d in summary.Dividends.OrderBy(x => x.Date))
        {
            sb.Append("Dividend,")
              .Append(d.Date.ToString("yyyy-MM-dd")).Append(',')
              .Append(Csv(d.Symbol)).Append(',')
              .Append(Csv(d.Exchange)).Append(',')
              .Append(Csv(d.Country)).Append(',')
              .Append(d.Amount.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(d.IsOverseas ? "Y" : "N")
              .Append('\n');
        }

        // Section 2: Capital-gain records
        foreach (var c in summary.CapitalGains.OrderBy(x => x.Date))
        {
            sb.Append("CapitalGain,")
              .Append(c.Date.ToString("yyyy-MM-dd")).Append(',')
              .Append(Csv(c.Symbol)).Append(',')
              .Append(Csv(c.Exchange)).Append(',')
              .Append(Csv(c.Country)).Append(',')
              .Append(c.RealizedPnl.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(c.IsOverseas ? "Y" : "N")
              .Append('\n');
        }

        // Footer: bucket totals + AMT trigger
        sb.AppendLine();
        sb.AppendLine("Summary,Label,Amount");
        sb.Append("Summary,Domestic Dividend,").Append(summary.DomesticDividendTotal.ToString("F2", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("Summary,Overseas Dividend,").Append(summary.OverseasDividendTotal.ToString("F2", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("Summary,Domestic Capital Gain,").Append(summary.DomesticCapitalGainTotal.ToString("F2", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("Summary,Overseas Capital Gain,").Append(summary.OverseasCapitalGainTotal.ToString("F2", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("Summary,Overseas Income Total,").Append(summary.OverseasIncomeTotal.ToString("F2", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("Summary,AMT Declaration Required,").Append(summary.TriggersAmtDeclaration ? "Y" : "N").Append('\n');

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8, ct).ConfigureAwait(false);
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

/// <summary>
/// PDF layout for the annual <see cref="TaxSummary"/>: 4 KPI bucket cards
/// (domestic / overseas dividend + capital gain) + AMT-trigger banner +
/// two record tables (Dividends, CapitalGains). Uses the same QuestPDF
/// chrome conventions as <see cref="StatementDocument"/> for visual
/// consistency.
/// </summary>
internal sealed class TaxSummaryDocument(TaxSummary summary) : IDocument
{
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
                col.Item().Text("Tax Summary").FontSize(18).SemiBold();
                col.Item().Text($"Year {summary.Year}")
                    .FontSize(10).FontColor(Colors.Grey.Darken1);
            });

            page.Content().PaddingTop(12).Column(col =>
            {
                // KPI grid — 2x2 totals
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(cd =>
                    {
                        cd.RelativeColumn();
                        cd.RelativeColumn();
                    });
                    table.Cell().Text(t =>
                    {
                        t.Span("Domestic Dividend\n").SemiBold();
                        t.Span(summary.DomesticDividendTotal.ToString("N2", CultureInfo.InvariantCulture));
                    });
                    table.Cell().Text(t =>
                    {
                        t.Span("Overseas Dividend\n").SemiBold();
                        t.Span(summary.OverseasDividendTotal.ToString("N2", CultureInfo.InvariantCulture));
                    });
                    table.Cell().Text(t =>
                    {
                        t.Span("Domestic Capital Gain\n").SemiBold();
                        t.Span(summary.DomesticCapitalGainTotal.ToString("N2", CultureInfo.InvariantCulture));
                    });
                    table.Cell().Text(t =>
                    {
                        t.Span("Overseas Capital Gain\n").SemiBold();
                        t.Span(summary.OverseasCapitalGainTotal.ToString("N2", CultureInfo.InvariantCulture));
                    });
                });

                // Overseas income + AMT banner
                col.Item().PaddingTop(12).BorderTop(1).BorderColor(Colors.Grey.Darken2)
                   .PaddingTop(6).Row(row =>
                {
                    row.RelativeItem().Text(t =>
                    {
                        t.Span("Overseas Income Total: ").SemiBold();
                        t.Span(summary.OverseasIncomeTotal.ToString("N2", CultureInfo.InvariantCulture));
                    });
                    row.ConstantItem(220).AlignRight().Text(t =>
                    {
                        if (summary.TriggersAmtDeclaration)
                        {
                            t.Span("⚠ AMT declaration required (≥ 1,000,000 NTD)")
                                .SemiBold().FontColor(Colors.Red.Darken2);
                        }
                        else
                        {
                            t.Span("AMT not triggered").FontColor(Colors.Grey.Darken1);
                        }
                    });
                });

                // Dividend records table
                if (summary.Dividends.Count > 0)
                {
                    col.Item().PaddingTop(14).Text("Dividend Records").SemiBold().FontSize(12);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cd =>
                        {
                            cd.ConstantColumn(80);
                            cd.RelativeColumn(2);
                            cd.RelativeColumn(1);
                            cd.ConstantColumn(50);
                            cd.RelativeColumn(1);
                        });
                        table.Header(h =>
                        {
                            h.Cell().Text("Date").SemiBold();
                            h.Cell().Text("Symbol").SemiBold();
                            h.Cell().Text("Exchange").SemiBold();
                            h.Cell().Text("Country").SemiBold();
                            h.Cell().AlignRight().Text("Amount").SemiBold();
                        });
                        foreach (var d in summary.Dividends.OrderBy(x => x.Date))
                        {
                            table.Cell().Text(d.Date.ToString("yyyy-MM-dd"));
                            table.Cell().Text(d.Symbol);
                            table.Cell().Text(d.Exchange);
                            table.Cell().Text(d.Country);
                            table.Cell().AlignRight().Text(d.Amount.ToString("N2", CultureInfo.InvariantCulture));
                        }
                    });
                }

                // Capital-gain records table
                if (summary.CapitalGains.Count > 0)
                {
                    col.Item().PaddingTop(14).Text("Realised Capital Gains").SemiBold().FontSize(12);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cd =>
                        {
                            cd.ConstantColumn(80);
                            cd.RelativeColumn(2);
                            cd.RelativeColumn(1);
                            cd.ConstantColumn(50);
                            cd.RelativeColumn(1);
                        });
                        table.Header(h =>
                        {
                            h.Cell().Text("Date").SemiBold();
                            h.Cell().Text("Symbol").SemiBold();
                            h.Cell().Text("Exchange").SemiBold();
                            h.Cell().Text("Country").SemiBold();
                            h.Cell().AlignRight().Text("Realised P&L").SemiBold();
                        });
                        foreach (var c in summary.CapitalGains.OrderBy(x => x.Date))
                        {
                            table.Cell().Text(c.Date.ToString("yyyy-MM-dd"));
                            table.Cell().Text(c.Symbol);
                            table.Cell().Text(c.Exchange);
                            table.Cell().Text(c.Country);
                            table.Cell().AlignRight().Text(c.RealizedPnl.ToString("N2", CultureInfo.InvariantCulture));
                        }
                    });
                }
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
