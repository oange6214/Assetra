using System.Windows;
using Assetra.WPF.Infrastructure;

namespace Assetra.WPF.Features.Snackbar;

public sealed class SnackbarService : ISnackbarService
{
    private readonly SnackbarViewModel _vm;

    public SnackbarService(SnackbarViewModel vm) => _vm = vm;

    public void Show(string message, SnackbarKind kind = SnackbarKind.Info)
        => System.Windows.Application.Current.Dispatcher.Invoke(() => _vm.Show(message, kind));

    public void Success(string message) => Show(message, SnackbarKind.Success);
    public void Warning(string message) => Show(message, SnackbarKind.Warning);
    public void Error(string message) => Show(message, SnackbarKind.Error);
}
