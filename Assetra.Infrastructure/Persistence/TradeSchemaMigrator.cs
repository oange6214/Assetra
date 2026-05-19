using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class TradeSchemaMigrator
{
    private static readonly HashSet<string> AllowedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "cash_amount", "cash_account_id", "note", "portfolio_entry_id",
        "created_at", "updated_at", "commission", "commission_discount",
        "liability_account_id", "principal", "interest_paid", "to_cash_account_id",
        "loan_label", "liability_asset_id", "parent_trade_id",
        "category_id", "recurring_source_id",
        "version", "last_modified_at", "last_modified_by_device", "is_deleted", "is_pending_push",
        // MultiCurrency-Trade-Refactor P1
        "instrument_currency", "commission_currency", "fx_rate",
        // Portfolio-Groups-Refactor P1
        "portfolio_group_id",
        // MultiCurrency-Reporting P4.5b — realized PnL split into market vs FX
        "realized_market_pnl", "realized_fx_pnl",
    };

    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "REAL", "TEXT", "TEXT NOT NULL DEFAULT ''",
        "TEXT NOT NULL DEFAULT 'TWD'",
        "INTEGER NOT NULL DEFAULT 0",
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
                CREATE TABLE IF NOT EXISTS trade (
                    id                   TEXT PRIMARY KEY,
                    symbol               TEXT NOT NULL,
                    exchange             TEXT NOT NULL,
                    name                 TEXT NOT NULL,
                    trade_type           TEXT NOT NULL,
                    trade_date           TEXT NOT NULL,
                    price                REAL NOT NULL,
                    quantity             INTEGER NOT NULL,
                    realized_pnl         REAL,
                    realized_pnl_pct     REAL,
                    cash_amount          REAL,
                    cash_account_id      TEXT,
                    note                 TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_trade_symbol ON trade (symbol);
                CREATE INDEX IF NOT EXISTS idx_trade_date   ON trade (trade_date DESC);
                """;
            cmd.ExecuteNonQuery();

            MigrateAddColumn(conn, tx, "cash_amount", "REAL");
            MigrateAddColumn(conn, tx, "cash_account_id", "TEXT");
            MigrateAddColumn(conn, tx, "note", "TEXT");
            MigrateAddColumn(conn, tx, "portfolio_entry_id", "TEXT");
            MigrateAddColumn(conn, tx, "created_at", "TEXT NOT NULL DEFAULT ''");
            MigrateAddColumn(conn, tx, "updated_at", "TEXT NOT NULL DEFAULT ''");
            MigrateAddColumn(conn, tx, "commission", "REAL");
            MigrateAddColumn(conn, tx, "commission_discount", "REAL");
            MigrateAddColumn(conn, tx, "liability_account_id", "TEXT");
            MigrateAddColumn(conn, tx, "principal", "REAL");
            MigrateAddColumn(conn, tx, "interest_paid", "REAL");
            MigrateAddColumn(conn, tx, "to_cash_account_id", "TEXT");
            MigrateAddColumn(conn, tx, "loan_label", "TEXT");
            MigrateAddColumn(conn, tx, "liability_asset_id", "TEXT");
            MigrateAddColumn(conn, tx, "parent_trade_id", "TEXT");
            MigrateAddColumn(conn, tx, "category_id", "TEXT");
            MigrateAddColumn(conn, tx, "recurring_source_id", "TEXT");

            // MultiCurrency-Trade-Refactor P1：標的計價幣別 + 手續費幣別 + FX rate。
            // 預設 instrument_currency='TWD' 讓既有資料保有可解讀語意；P2 backfill
            // 會依 exchange 把外幣標的（NYSE/NASDAQ/HKEX/TSE）改成對應幣別。
            // commission_currency / fx_rate 預設 NULL 表示「與標的幣別相同 / 1.0」。
            MigrateAddColumn(conn, tx, "instrument_currency", "TEXT NOT NULL DEFAULT 'TWD'");
            MigrateAddColumn(conn, tx, "commission_currency", "TEXT");
            MigrateAddColumn(conn, tx, "fx_rate", "REAL");

            // Portfolio-Groups-Refactor P1 — 每筆 trade 屬於一個 portfolio_group。
            // nullable，既有 row 預設 NULL，下方 backfill 把它們設成 DefaultGroupId。
            MigrateAddColumn(conn, tx, "portfolio_group_id", "TEXT");

            // MultiCurrency-Reporting P4.5b — realized PnL split into
            // "market gain" (your stock pick) + "fx gain" (currency drift).
            // Both nullable — only populated for sells with FX history coverage.
            // Existing rows stay NULL → UI shows "—".
            MigrateAddColumn(conn, tx, "realized_market_pnl", "REAL");
            MigrateAddColumn(conn, tx, "realized_fx_pnl", "REAL");

            // Sync columns (v0.20.7) — mirror Category schema
            MigrateAddColumn(conn, tx, "version", "INTEGER NOT NULL DEFAULT 0");
            MigrateAddColumn(conn, tx, "last_modified_at", "TEXT NOT NULL DEFAULT ''");
            MigrateAddColumn(conn, tx, "last_modified_by_device", "TEXT NOT NULL DEFAULT ''");
            MigrateAddColumn(conn, tx, "is_deleted", "INTEGER NOT NULL DEFAULT 0");
            MigrateAddColumn(conn, tx, "is_pending_push", "INTEGER NOT NULL DEFAULT 0");

            cmd.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_trade_cash_acct ON trade (cash_account_id);
                CREATE INDEX IF NOT EXISTS idx_trade_loan_label ON trade (loan_label);
                CREATE INDEX IF NOT EXISTS idx_trade_liability_asset ON trade (liability_asset_id);
                CREATE INDEX IF NOT EXISTS idx_trade_category ON trade (category_id);
                CREATE INDEX IF NOT EXISTS idx_trade_recurring_source ON trade (recurring_source_id);
                CREATE INDEX IF NOT EXISTS idx_trade_pending ON trade (is_pending_push) WHERE is_pending_push = 1;
                """;
            cmd.ExecuteNonQuery();

            BackfillLegacyLiabilityLinks(conn, tx, cmd);
            BackfillLiabilityAssetIds(cmd);
            BackfillLoanLabels(conn, tx, cmd);
            NormalizeLegacyInterestTrades(cmd);
            BackfillIncomeTradeNameFromCashAccount(conn, tx, cmd);
            FixWronglyTaggedUsTickers(cmd);
            BackfillInstrumentCurrencyFromExchange(cmd);
            BackfillPortfolioGroupId(cmd);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static void BackfillLegacyLiabilityLinks(
        SqliteConnection conn, SqliteTransaction tx, SqliteCommand cmd)
    {
        if (!TableExists(conn, tx, "liability_account"))
            return;

        cmd.CommandText = """
            UPDATE trade
               SET liability_account_id = (
                   SELECT id FROM liability_account
                    WHERE liability_account.name = trade.name
                    LIMIT 1
               )
             WHERE trade_type = 'LoanBorrow'
               AND liability_account_id IS NULL
               AND name IN (SELECT name FROM liability_account);
            """;
        cmd.ExecuteNonQuery();
    }

    private static void BackfillLoanLabels(
        SqliteConnection conn, SqliteTransaction tx, SqliteCommand cmd)
    {
        if (TableExists(conn, tx, "liability_account"))
        {
            cmd.CommandText = """
                UPDATE trade
                   SET loan_label = (
                       SELECT name FROM liability_account
                        WHERE liability_account.id = trade.liability_account_id
                        LIMIT 1
                   )
                 WHERE trade_type IN ('LoanBorrow', 'LoanRepay')
                   AND liability_account_id IS NOT NULL
                   AND loan_label IS NULL;
                """;
            cmd.ExecuteNonQuery();
            return;
        }

        if (!TableExists(conn, tx, "asset"))
            return;

        cmd.CommandText = """
            UPDATE trade
               SET loan_label = (
                   SELECT name FROM asset
                    WHERE asset.id = trade.liability_account_id
                    LIMIT 1
               )
             WHERE trade_type IN ('LoanBorrow', 'LoanRepay')
               AND liability_account_id IS NOT NULL
               AND loan_label IS NULL;
            """;
        cmd.ExecuteNonQuery();
    }

    private static void BackfillLiabilityAssetIds(SqliteCommand cmd)
    {
        cmd.CommandText = """
            UPDATE trade
               SET liability_asset_id = liability_account_id
             WHERE liability_asset_id IS NULL
               AND liability_account_id IS NOT NULL;
            """;
        cmd.ExecuteNonQuery();
    }

    private static void NormalizeLegacyInterestTrades(SqliteCommand cmd)
    {
        cmd.CommandText = """
            UPDATE trade
               SET trade_type  = 'Withdrawal',
                   cash_amount = CASE
                       WHEN cash_amount IS NULL THEN NULL
                       ELSE ABS(cash_amount)
                   END
             WHERE trade_type = 'Interest';
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// One-time backfill for Income trades created BEFORE the Income decouple
    /// refactor (commit 7fe6647). Pre-refactor, RecordIncomeAsync stored
    /// <c>Trade.Name = Note</c> (e.g. "薪資") so the trade-list "資產" column
    /// showed the note rather than the cash account. New behaviour stores
    /// <c>Name = AccountName</c> (e.g. "台新 Richart") to match Deposit /
    /// Withdrawal convention.
    ///
    /// This migration finds Income rows where the linked cash account exists
    /// AND <c>name</c> still equals <c>note</c> (the legacy fingerprint) AND
    /// rewrites <c>name</c> to the account's name. Income rows without a
    /// linked cash account, or where <c>name</c> already differs from
    /// <c>note</c> (post-refactor), are left untouched.
    ///
    /// Safe to run on every startup — the WHERE clause makes it idempotent.
    /// </summary>
    private static void BackfillIncomeTradeNameFromCashAccount(
        SqliteConnection conn, SqliteTransaction tx, SqliteCommand cmd)
    {
        if (!TableExists(conn, tx, "asset"))
            return;

        cmd.CommandText = """
            UPDATE trade
               SET name = (
                       SELECT a.name
                         FROM asset a
                        WHERE a.id = trade.cash_account_id
                   )
             WHERE trade_type      = 'Income'
               AND cash_account_id IS NOT NULL
               AND note            IS NOT NULL
               AND name            =  note
               AND EXISTS (
                       SELECT 1
                         FROM asset a
                        WHERE a.id = trade.cash_account_id
                   );
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 修復「使用者輸入美股 ticker 但沒從 autocomplete 選，被 InferExchange 預設成 TWSE」的歷史資料。
    /// 觸發條件（保守）：
    ///   - exchange 目前是 TWSE 或 TPEX（台灣 venue tag）
    ///   - symbol 長度 1–5、完全沒有數字、首字母是英文字母
    /// → 改為 NASDAQ。所有台股代號都含數字（2330 / 00981A / 0050），不會誤判。
    /// 之後 <c>BackfillInstrumentCurrencyFromExchange</c> 會把新的 NASDAQ row 自動補上 instrument_currency='USD'。
    /// Idempotent — 已修正過的 row（exchange 已是 NASDAQ）跳過 WHERE clause 不會重複處理。
    /// 同時修 portfolio + portfolio_position_log 兩個關聯表（如果存在），確保所有 read path 都看到正確 exchange。
    /// </summary>
    private static void FixWronglyTaggedUsTickers(SqliteCommand cmd)
    {
        var conn = cmd.Connection!;
        var tx = cmd.Transaction!;

        // The shared filter clause — captured once so all three tables apply the
        // exact same heuristic, no chance of drift.
        const string filterClause = """
                     UPPER(exchange) IN ('TWSE', 'TPEX')
                 AND LENGTH(symbol) BETWEEN 1 AND 5
                 AND symbol NOT GLOB '*[0-9]*'
                 AND symbol GLOB '[A-Za-z]*'
            """;

        cmd.CommandText = $"""
            UPDATE trade
               SET exchange = 'NASDAQ'
             WHERE {filterClause};
            """;
        cmd.ExecuteNonQuery();

        if (TableExists(conn, tx, "portfolio"))
        {
            cmd.CommandText = $"""
                UPDATE portfolio
                   SET exchange = 'NASDAQ',
                       currency = 'USD'
                 WHERE {filterClause};
                """;
            cmd.ExecuteNonQuery();
        }

        if (TableExists(conn, tx, "portfolio_position_log"))
        {
            cmd.CommandText = $"""
                UPDATE portfolio_position_log
                   SET exchange = 'NASDAQ'
                 WHERE {filterClause};
                """;
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// MultiCurrency-Trade-Refactor P2：把外幣交易所的歷史 trade row 從預設的
    /// <c>instrument_currency='TWD'</c> 修正為對應的標的幣別。
    /// 跟 <see cref="Assetra.Infrastructure.History.ExchangeCurrencyResolver"/> 的對照表保持一致。
    /// idempotent — 只動 instrument_currency 仍為 'TWD' 且 exchange 為外幣交易所的列；
    /// 使用者後續手動改幣別後不會被回填覆寫（因為 WHERE clause 不會抓到非 TWD 列）。
    /// </summary>
    /// <summary>
    /// Portfolio-Groups-Refactor P1：把既有 trades 的 portfolio_group_id (NULL) 設成
    /// 預設群組 (PortfolioGroup.DefaultId)。Idempotent — 已設過群組的 row 跳過。
    /// 預設群組由 PortfolioGroupSchemaMigrator 在 db init 時 INSERT OR IGNORE seed。
    /// </summary>
    private static void BackfillPortfolioGroupId(SqliteCommand cmd)
    {
        cmd.CommandText = """
            UPDATE trade
               SET portfolio_group_id = '00000000-0000-0000-0000-000000000001'
             WHERE portfolio_group_id IS NULL;
            """;
        cmd.ExecuteNonQuery();
    }

    private static void BackfillInstrumentCurrencyFromExchange(SqliteCommand cmd)
    {
        cmd.CommandText = """
            UPDATE trade
               SET instrument_currency = CASE UPPER(exchange)
                       WHEN 'NYSE'     THEN 'USD'
                       WHEN 'NASDAQ'   THEN 'USD'
                       WHEN 'NYSEARCA' THEN 'USD'
                       WHEN 'AMEX'     THEN 'USD'
                       WHEN 'BATS'     THEN 'USD'
                       WHEN 'IEX'      THEN 'USD'
                       WHEN 'HKEX'     THEN 'HKD'
                       WHEN 'TSE'      THEN 'JPY'
                       ELSE instrument_currency
                   END
             WHERE instrument_currency = 'TWD'
               AND UPPER(exchange) IN
                   ('NYSE','NASDAQ','NYSEARCA','AMEX','BATS','IEX','HKEX','TSE');
            """;
        cmd.ExecuteNonQuery();
    }

    private static void MigrateAddColumn(
        SqliteConnection conn, SqliteTransaction tx, string column, string type)
    {
        if (!AllowedColumns.Contains(column) || !AllowedTypes.Contains(type))
            throw new ArgumentException($"Invalid column or type: {column} {type}");

        using var check = conn.CreateCommand();
        check.Transaction = tx;
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('trade') WHERE name = $col;";
        check.Parameters.AddWithValue("$col", column);
        if ((long)(check.ExecuteScalar() ?? 0L) > 0)
            return;

        using var alter = conn.CreateCommand();
        alter.Transaction = tx;
        alter.CommandText = $"ALTER TABLE trade ADD COLUMN {column} {type};";
        alter.ExecuteNonQuery();
    }

    private static bool TableExists(SqliteConnection conn, SqliteTransaction tx, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        cmd.Parameters.AddWithValue("$name", tableName);
        return cmd.ExecuteScalar() is not null;
    }
}
