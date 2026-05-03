using Assetra.WPF.Infrastructure;
using WpfApplication = System.Windows.Application;

namespace Assetra.WPF.Features.Snackbar;

public sealed class SnackbarService : ISnackbarService
{
    private readonly SnackbarViewModel _vm;

    public SnackbarService(SnackbarViewModel vm) => _vm = vm;

    public void Show(string message, SnackbarKind kind = SnackbarKind.Info)
    {
        var dispatcher = WpfApplication.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            return;

        void ShowCore() => _vm.Show(message, kind);

        if (dispatcher.CheckAccess())
        {
            ShowCore();
            return;
        }

        dispatcher.BeginInvoke(ShowCore);
    }

    public void Success(string message) => Show(message, SnackbarKind.Success);
    public void Warning(string message) => Show(message, SnackbarKind.Warning);
    public void Error(string message) => Show(message, SnackbarKind.Error);
}
