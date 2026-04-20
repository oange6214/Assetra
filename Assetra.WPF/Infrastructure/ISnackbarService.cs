namespace Assetra.WPF.Infrastructure;

public enum SnackbarKind { Info, Success, Warning, Error }

public interface ISnackbarService
{
    void Show(string message, SnackbarKind kind = SnackbarKind.Info);
    void Success(string message);
    void Warning(string message);
    void Error(string message);
}
