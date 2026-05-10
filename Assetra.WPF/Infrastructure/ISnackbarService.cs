namespace Assetra.WPF.Infrastructure;

public enum SnackbarKind { Info, Success, Warning, Error }

public interface ISnackbarService
{
    void Show(string message, SnackbarKind kind = SnackbarKind.Info);
    /// <summary>
    /// 顯示帶 action 按鈕的 snackbar。例如「已新增買入交易」 + 「查看交易」按鈕跳到交易記錄頁。
    /// 按下 actionLabel 按鈕會呼叫 onAction 並關閉 snackbar。
    /// </summary>
    void Show(string message, string actionLabel, Action onAction, SnackbarKind kind = SnackbarKind.Success);
    void Success(string message);
    void Warning(string message);
    void Error(string message);
}
