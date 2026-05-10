using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class AssetSchemaMigrator
{
    // Fixed UUIDs for system groups — deterministic so INSERT OR IGNORE is idempotent.
    // 資金帳戶細分（v0.28+）：原「銀行帳戶」拆成「銀行類」+ 3 個新 group。
    // 舊 GrpBankAccount 保留 ID 但 name 改為「銀行類」(SeedSystemGroups 會 UPDATE)。
    public static readonly Guid GrpBankAccount = new("11111111-1111-1111-1111-111111111101");
    public static readonly Guid GrpCashOnHand = new("11111111-1111-1111-1111-111111111104");
    public static readonly Guid GrpBrokerageSettlement = new("11111111-1111-1111-1111-111111111105");
    public static readonly Guid GrpEPayment = new("11111111-1111-1111-1111-111111111106");
    public static readonly Guid GrpRealEstate = new("11111111-1111-1111-1111-111111111102");
    public static readonly Guid GrpVehicle = new("11111111-1111-1111-1111-111111111103");
    public static readonly Guid GrpBankLoan = new("11111111-1111-1111-1111-111111111201");
    public static readonly Guid GrpCreditCard = new("11111111-1111-1111-1111-111111111202");

    private static readonly HashSet<string> AssetAllowedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "is_active", "updated_at",
        "loan_annual_rate", "loan_term_months", "loan_start_date", "loan_handling_fee",
        "liability_subtype", "billing_day", "due_day", "credit_limit", "issuer_name",
        "subtype",
        "version", "last_modified_at", "last_modified_by_device", "is_deleted", "is_pending_push",
    };

    private static readonly HashSet<string> AssetAllowedTypeDefs = new(StringComparer.OrdinalIgnoreCase)
    {
        "INTEGER NOT NULL DEFAULT 1",
        "INTEGER NOT NULL DEFAULT 0",
        "TEXT NOT NULL DEFAULT ''",
        "REAL",
        "INTEGER",
        "TEXT",
    };

    public static void EnsureInitialized(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;

            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS asset_group (
                    id           TEXT PRIMARY KEY,
                    name         TEXT NOT NULL,
                    asset_type   TEXT NOT NULL,
                    icon         TEXT,
                    sort_order   INTEGER NOT NULL DEFAULT 0,
                    is_system    INTEGER NOT NULL DEFAULT 0,
                    created_date TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS asset (
                    id           TEXT PRIMARY KEY,
                    name         TEXT NOT NULL,
                    asset_type   TEXT NOT NULL,
                    group_id     TEXT REFERENCES asset_group(id),
                    currency     TEXT NOT NULL DEFAULT 'TWD',
                    created_date TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS asset_event (
                    id               TEXT PRIMARY KEY,
                    asset_id         TEXT NOT NULL REFERENCES asset(id),
                    event_type       TEXT NOT NULL,
                    event_date       TEXT NOT NULL,
                    amount           REAL,
                    quantity         REAL,
                    note             TEXT,
                    cash_account_id  TEXT,
                    created_at       TEXT NOT NULL DEFAULT ''
                );

                CREATE INDEX IF NOT EXISTS idx_asset_type     ON asset (asset_type);
                CREATE INDEX IF NOT EXISTS idx_asset_event_id ON asset_event (asset_id);
                """;
            cmd.ExecuteNonQuery();

            SeedSystemGroups(cmd);
            AddAssetMetadataColumns(conn, tx);
            AddGroupSyncColumns(conn, tx);
            AddEventSyncColumns(conn, tx);
            EnsureAssetIndexes(conn, tx);
            MigrateLegacyCashAccounts(cmd, conn, tx);
            MigrateLegacyLiabilityAccounts(cmd, conn, tx);
            BackfillLiabilitySubtype(cmd);
            // 必須在 subtype 欄位存在之後才能跑（依 subtype 重新分配 group_id）。
            ReclassifyCashAccountsBySubtype(cmd);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static void SeedSystemGroups(SqliteCommand cmd)
    {
        // INSERT OR IGNORE：第一次跑全建；舊 DB 已有 GrpBankAccount，所以原本的「銀行帳戶」不會被覆蓋。
        // 後續另行 UPDATE 把舊 GrpBankAccount 的 name 從「銀行帳戶」改為「銀行類」（保 ID 以保持外鍵）。
        cmd.CommandText = """
            INSERT OR IGNORE INTO asset_group (id, name, asset_type, icon, sort_order, is_system, created_date) VALUES
                ($g1, '銀行類',     'Asset',     '🏦', 0, 1, '2026-01-01'),
                ($g6, '證券交割款', 'Asset',     '📊', 1, 1, '2026-01-01'),
                ($g7, '電子支付',   'Asset',     '📱', 2, 1, '2026-01-01'),
                ($g8, '手邊現金',   'Asset',     '💵', 3, 1, '2026-01-01'),
                ($g2, '不動產',     'Asset',     '🏠', 4, 1, '2026-01-01'),
                ($g3, '交通工具',   'Asset',     '🚗', 5, 1, '2026-01-01'),
                ($g4, '銀行貸款',   'Liability', '🏦', 0, 1, '2026-01-01'),
                ($g5, '信用卡',     'Liability', '💳', 1, 1, '2026-01-01');
            """;
        cmd.Parameters.AddWithValue("$g1", GrpBankAccount.ToString());
        cmd.Parameters.AddWithValue("$g2", GrpRealEstate.ToString());
        cmd.Parameters.AddWithValue("$g3", GrpVehicle.ToString());
        cmd.Parameters.AddWithValue("$g4", GrpBankLoan.ToString());
        cmd.Parameters.AddWithValue("$g5", GrpCreditCard.ToString());
        cmd.Parameters.AddWithValue("$g6", GrpBrokerageSettlement.ToString());
        cmd.Parameters.AddWithValue("$g7", GrpEPayment.ToString());
        cmd.Parameters.AddWithValue("$g8", GrpCashOnHand.ToString());
        cmd.ExecuteNonQuery();
        cmd.Parameters.Clear();

        // 舊 DB 的 GrpBankAccount 之 name 還是「銀行帳戶」，把它改名為「銀行類」（保留 ID 與所有 FK）。
        // 同時更新 sort_order 對齊新 taxonomy。is_system=1 的不允許使用者改名，所以這個 UPDATE 是必要的。
        cmd.CommandText = """
            UPDATE asset_group
               SET name = '銀行類', sort_order = 0
             WHERE id = $bankId AND name = '銀行帳戶' AND is_system = 1;
            UPDATE asset_group SET sort_order = 4 WHERE id = $realEstateId AND is_system = 1;
            UPDATE asset_group SET sort_order = 5 WHERE id = $vehicleId AND is_system = 1;
            """;
        cmd.Parameters.AddWithValue("$bankId", GrpBankAccount.ToString());
        cmd.Parameters.AddWithValue("$realEstateId", GrpRealEstate.ToString());
        cmd.Parameters.AddWithValue("$vehicleId", GrpVehicle.ToString());
        cmd.ExecuteNonQuery();
        cmd.Parameters.Clear();

    }

    /// <summary>
    /// 既有 cash 帳戶 reclassify：依 subtype 把 group_id 設定到正確的細分群組。
    /// - 只動 group_id 為 NULL 或還在舊 GrpBankAccount 的（避免覆蓋使用者已自訂的）。
    /// - subtype=null 或無對應的 → 維持不動（保留 NULL → 落入「其他」）。
    /// 必須在 subtype 欄位被 AddAssetMetadataColumns 加入後才能跑。
    /// </summary>
    private static void ReclassifyCashAccountsBySubtype(SqliteCommand cmd)
    {
        cmd.CommandText = """
            UPDATE asset
               SET group_id = $cashOnHand
             WHERE asset_type = 'cash' AND subtype IN ('現金', '手邊現金')
               AND (group_id IS NULL OR group_id = $bankAccount);

            UPDATE asset
               SET group_id = $brokerage
             WHERE asset_type = 'cash' AND subtype = '證券交割戶'
               AND (group_id IS NULL OR group_id = $bankAccount);

            UPDATE asset
               SET group_id = $epayment
             WHERE asset_type = 'cash' AND subtype IN ('電子支付', '儲值卡')
               AND (group_id IS NULL OR group_id = $bankAccount);

            UPDATE asset
               SET group_id = $bankAccount
             WHERE asset_type = 'cash' AND subtype IN ('銀行活存', '數位活存', '定期存款', '外幣活存')
               AND group_id IS NULL;
            """;
        cmd.Parameters.AddWithValue("$cashOnHand", GrpCashOnHand.ToString());
        cmd.Parameters.AddWithValue("$brokerage", GrpBrokerageSettlement.ToString());
        cmd.Parameters.AddWithValue("$epayment", GrpEPayment.ToString());
        cmd.Parameters.AddWithValue("$bankAccount", GrpBankAccount.ToString());
        cmd.ExecuteNonQuery();
        cmd.Parameters.Clear();
    }

    private static void AddAssetMetadataColumns(SqliteConnection conn, SqliteTransaction tx)
    {
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
            "is_active", "INTEGER NOT NULL DEFAULT 1",
            AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
            "updated_at", "TEXT NOT NULL DEFAULT ''",
            AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
            "loan_annual_rate", "REAL", AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
            "loan_term_months", "INTEGER", AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
            "loan_start_date", "TEXT", AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
            "loan_handling_fee", "REAL", AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
            "liability_subtype", "TEXT",
            AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
            "billing_day", "INTEGER", AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
            "due_day", "INTEGER", AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
            "credit_limit", "REAL", AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
            "issuer_name", "TEXT", AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
            "subtype", "TEXT", AssetAllowedColumns, AssetAllowedTypeDefs);

        // Sync metadata (v0.20.8) — stamp version/last_modified, soft delete, pending push.
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
            "version", "INTEGER NOT NULL DEFAULT 0", AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
            "last_modified_at", "TEXT NOT NULL DEFAULT ''", AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
            "last_modified_by_device", "TEXT NOT NULL DEFAULT ''", AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
            "is_deleted", "INTEGER NOT NULL DEFAULT 0", AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset",
            "is_pending_push", "INTEGER NOT NULL DEFAULT 0", AssetAllowedColumns, AssetAllowedTypeDefs);
    }

    private static void AddGroupSyncColumns(SqliteConnection conn, SqliteTransaction tx)
    {
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset_group",
            "version", "INTEGER NOT NULL DEFAULT 0", AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset_group",
            "last_modified_at", "TEXT NOT NULL DEFAULT ''", AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset_group",
            "last_modified_by_device", "TEXT NOT NULL DEFAULT ''", AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset_group",
            "is_deleted", "INTEGER NOT NULL DEFAULT 0", AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset_group",
            "is_pending_push", "INTEGER NOT NULL DEFAULT 0", AssetAllowedColumns, AssetAllowedTypeDefs);
    }

    private static void AddEventSyncColumns(SqliteConnection conn, SqliteTransaction tx)
    {
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset_event",
            "version", "INTEGER NOT NULL DEFAULT 0", AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset_event",
            "last_modified_at", "TEXT NOT NULL DEFAULT ''", AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset_event",
            "last_modified_by_device", "TEXT NOT NULL DEFAULT ''", AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset_event",
            "is_deleted", "INTEGER NOT NULL DEFAULT 0", AssetAllowedColumns, AssetAllowedTypeDefs);
        SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset_event",
            "is_pending_push", "INTEGER NOT NULL DEFAULT 0", AssetAllowedColumns, AssetAllowedTypeDefs);
    }

    private static void EnsureAssetIndexes(SqliteConnection conn, SqliteTransaction tx)
    {
        using var idx = conn.CreateCommand();
        idx.Transaction = tx;
        idx.CommandText = """
            CREATE UNIQUE INDEX IF NOT EXISTS idx_asset_name_currency_asset
            ON asset(name, currency) WHERE asset_type = 'Asset'
            """;
        idx.ExecuteNonQuery();

        using var pendingIdx = conn.CreateCommand();
        pendingIdx.Transaction = tx;
        pendingIdx.CommandText =
            "CREATE INDEX IF NOT EXISTS idx_asset_pending ON asset (is_pending_push) WHERE is_pending_push = 1;";
        pendingIdx.ExecuteNonQuery();

        using var grpPending = conn.CreateCommand();
        grpPending.Transaction = tx;
        grpPending.CommandText =
            "CREATE INDEX IF NOT EXISTS idx_asset_group_pending ON asset_group (is_pending_push) WHERE is_pending_push = 1;";
        grpPending.ExecuteNonQuery();

        using var evtPending = conn.CreateCommand();
        evtPending.Transaction = tx;
        evtPending.CommandText =
            "CREATE INDEX IF NOT EXISTS idx_asset_event_pending ON asset_event (is_pending_push) WHERE is_pending_push = 1;";
        evtPending.ExecuteNonQuery();
    }

    private static void MigrateLegacyCashAccounts(
        SqliteCommand cmd, SqliteConnection conn, SqliteTransaction tx)
    {
        if (!TableExists(conn, tx, "cash_account"))
            return;

        cmd.CommandText = $"""
            INSERT OR IGNORE INTO asset (id, name, asset_type, group_id, currency, created_date)
            SELECT id, name, 'Asset', '{GrpBankAccount}', COALESCE(currency, 'TWD'), created_date
            FROM cash_account;
            """;
        cmd.ExecuteNonQuery();

        cmd.CommandText = "DROP TABLE IF EXISTS cash_account;";
        cmd.ExecuteNonQuery();
    }

    private static void MigrateLegacyLiabilityAccounts(
        SqliteCommand cmd, SqliteConnection conn, SqliteTransaction tx)
    {
        if (!TableExists(conn, tx, "liability_account"))
            return;

        cmd.CommandText = $"""
            INSERT OR IGNORE INTO asset (id, name, asset_type, group_id, currency, created_date, liability_subtype)
            SELECT id, name, 'Liability', '{GrpBankLoan}', COALESCE(currency, 'TWD'),
                   COALESCE(created_date, date('now')), 'Loan'
            FROM liability_account;
            """;
        cmd.ExecuteNonQuery();

        cmd.CommandText = "DROP TABLE IF EXISTS liability_account;";
        cmd.ExecuteNonQuery();
    }

    private static void BackfillLiabilitySubtype(SqliteCommand cmd)
    {
        cmd.CommandText = """
            UPDATE asset
               SET liability_subtype = CASE
                   WHEN group_id = $credit_card_group THEN 'CreditCard'
                   ELSE 'Loan'
               END
             WHERE asset_type = 'Liability'
               AND (liability_subtype IS NULL OR liability_subtype = '');
            """;
        cmd.Parameters.AddWithValue("$credit_card_group", GrpCreditCard.ToString());
        cmd.ExecuteNonQuery();
        cmd.Parameters.Clear();
    }

    private static bool TableExists(SqliteConnection conn, SqliteTransaction tx, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$t;";
        cmd.Parameters.AddWithValue("$t", table);
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }
}
