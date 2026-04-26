using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Assetra.Core.Models.Import;

/// <summary>
/// 解析後尚未提交的單筆預覽列。
/// <para>
/// 銀行對帳：<see cref="Amount"/> 正號為收入、負號為支出，<see cref="Counterparty"/> 為對手方／摘要。
/// 券商明細：<see cref="Amount"/> 為交易金額（含手續費），<see cref="Symbol"/> 為標的，<see cref="Quantity"/> 為股數。
/// </para>
/// </summary>
public sealed record ImportPreviewRow(
    int RowIndex,
    DateOnly Date,
    decimal Amount,
    string? Counterparty,
    string? Memo,
    string? Symbol = null,
    decimal? Quantity = null,
    string? Currency = null)
{
    /// <summary>
    /// 去重 hash：日期 + 金額 + 對手方/摘要 + 標的。<br/>
    /// 大小寫不敏感、空白正規化，避免相同來源重複匯入。
    /// </summary>
    public string DedupeHash
    {
        get
        {
            var key = string.Join('|',
                Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Amount.ToString("0.####", CultureInfo.InvariantCulture),
                Normalize(Counterparty),
                Normalize(Memo),
                Normalize(Symbol));

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            return Convert.ToHexString(bytes);
        }
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}
