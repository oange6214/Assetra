using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Snackbar;

public sealed partial class SnackbarItemViewModel : ObservableObject
{
    public string Text { get; }
    public SnackbarKind Kind { get; }

    // Icon glyph (Segoe MDL2 Assets)
    public string IconGlyph => Kind switch
    {
        SnackbarKind.Success => "\uE73E",
        SnackbarKind.Warning => "\uE7BA",
        SnackbarKind.Error => "\uEA39",
        _ => "\uE946",   // Info
    };

    public Action<SnackbarItemViewModel>? OnDismiss { get; init; }

    [RelayCommand]
    private void Dismiss() => OnDismiss?.Invoke(this);

    public SnackbarItemViewModel(string text, SnackbarKind kind)
    {
        Text = text;
        Kind = kind;
    }
}
