namespace Assetra.Application.Portfolio.Contracts;

/// <summary>
/// 嘗試建立的資金帳戶，與一個「仍啟用中」的同名同幣別帳戶重複。
/// 由 UI 層攔截後顯示友善訊息，而非讓底層 SQLite UNIQUE 約束例外（Error 19）往上拋成 crash。
/// （已封存 / 已軟刪除的同名帳戶不會走到這裡——那種情況會被「就地復活」。）
/// </summary>
public sealed class DuplicateAccountException : Exception
{
    public string AccountName { get; }
    public string Currency { get; }

    public DuplicateAccountException(string accountName, string currency)
        : base($"An active account named '{accountName}' ({currency}) already exists.")
    {
        AccountName = accountName;
        Currency = currency;
    }
}
