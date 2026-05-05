using System.IO;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class RetirementAccountSqliteRepositorySyncTests
{
    [Fact]
    public async Task LocalDelete_StampsPendingTombstoneWithDeviceMetadata()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var modifiedAt = new DateTimeOffset(2026, 5, 3, 12, 0, 0, TimeSpan.Zero);
        var repo = new RetirementAccountSqliteRepository(
            dbPath,
            deviceIdProvider: () => "device-retirement",
            time: new FixedTimeProvider(modifiedAt));
        var account = MakeAccount();

        try
        {
            await repo.AddAsync(account);
            await repo.MarkPushedAsync([account.Id]);

            await repo.RemoveAsync(account.Id);
            var pending = await repo.GetPendingPushAsync();

            var envelope = Assert.Single(pending);
            Assert.True(envelope.Deleted);
            Assert.Equal(account.Id, envelope.EntityId);
            Assert.Equal(2, envelope.Version.Version);
            Assert.Equal(modifiedAt, envelope.Version.LastModifiedAt);
            Assert.Equal("device-retirement", envelope.Version.LastModifiedByDevice);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    private static RetirementAccount MakeAccount() =>
        new(
            Id: Guid.NewGuid(),
            Name: "Labor Pension",
            AccountType: RetirementAccountType.LaborPension,
            Provider: "Bureau",
            Balance: 1_000_000m,
            EmployeeContributionRate: 0.06m,
            EmployerContributionRate: 0.06m,
            YearsOfService: 5,
            LegalWithdrawalAge: 65,
            OpenedDate: new DateOnly(2020, 1, 1),
            Currency: "TWD",
            Status: RetirementAccountStatus.Active,
            Notes: null,
            Version: EntityVersion.Initial("old-device", DateTimeOffset.UnixEpoch));

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
