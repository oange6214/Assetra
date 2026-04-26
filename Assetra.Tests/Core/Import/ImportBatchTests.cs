using Assetra.Core.Models.Import;
using Xunit;

namespace Assetra.Tests.Core.Import;

public class ImportBatchTests
{
    private static ImportPreviewRow Row(int idx, decimal amount = -100m) =>
        new(idx, new DateOnly(2026, 4, 26), amount, "X", null);

    [Fact]
    public void NewRowCount_IsRowCountMinusConflicts()
    {
        var rows = new[] { Row(1), Row(2, -200m), Row(3, -300m) };
        var conflicts = new[] { new ImportConflict(rows[0], Guid.NewGuid(), null) };

        var batch = new ImportBatch(
            Guid.NewGuid(),
            "statement.csv",
            ImportFileType.Csv,
            ImportFormat.CathayUnitedBank,
            DateTimeOffset.UtcNow,
            rows,
            conflicts);

        Assert.Equal(3, batch.RowCount);
        Assert.Equal(1, batch.ConflictCount);
        Assert.Equal(2, batch.NewRowCount);
        Assert.Equal(ImportSourceKind.BankStatement, batch.SourceKind);
    }

    [Fact]
    public void SourceKind_MapsBrokerFormat_ToBrokerStatement()
    {
        var batch = new ImportBatch(
            Guid.NewGuid(),
            "trades.xlsx",
            ImportFileType.Excel,
            ImportFormat.YuantaSecurities,
            DateTimeOffset.UtcNow,
            Array.Empty<ImportPreviewRow>(),
            Array.Empty<ImportConflict>());

        Assert.Equal(ImportSourceKind.BrokerStatement, batch.SourceKind);
    }

    [Theory]
    [InlineData(ImportFormat.CathayUnitedBank, ImportSourceKind.BankStatement)]
    [InlineData(ImportFormat.EsunBank, ImportSourceKind.BankStatement)]
    [InlineData(ImportFormat.CtbcBank, ImportSourceKind.BankStatement)]
    [InlineData(ImportFormat.TaishinBank, ImportSourceKind.BankStatement)]
    [InlineData(ImportFormat.FubonBank, ImportSourceKind.BankStatement)]
    [InlineData(ImportFormat.YuantaSecurities, ImportSourceKind.BrokerStatement)]
    [InlineData(ImportFormat.FubonSecurities, ImportSourceKind.BrokerStatement)]
    [InlineData(ImportFormat.KgiSecurities, ImportSourceKind.BrokerStatement)]
    [InlineData(ImportFormat.SinoPacSecurities, ImportSourceKind.BrokerStatement)]
    [InlineData(ImportFormat.CapitalSecurities, ImportSourceKind.BrokerStatement)]
    public void Format_MapsToExpectedSourceKind(ImportFormat format, ImportSourceKind expected)
    {
        Assert.Equal(expected, format.ToSourceKind());
    }
}
