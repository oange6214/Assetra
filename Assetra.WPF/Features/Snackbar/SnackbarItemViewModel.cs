using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Snackbar;

public sealed partial class SnackbarItemViewModel : ObservableObject
{
    public string Text { get; }
    public SnackbarKind Kind { get; }

    /// <summary>Action 按鈕標籤（例：「查看交易」）。null = 不顯示 action 按鈕。</summary>
    public string? ActionLabel { get; }

    /// <summary>有 action 時 = true，XAML 用此控制按鈕 visibility。</summary>
    public bool HasAction => ActionLabel is not null && _onAction is not null;

    private readonly Action? _onAction;

    // Icon glyph (Segoe MDL2 Assets)
    public string IconGlyph => Kind switch
    {
        SnackbarKind.Success => "",
        SnackbarKind.Warning => "",
        SnackbarKind.Error => "",
        _ => "",   // Info
    };

    public Action<SnackbarItemViewModel>? OnDismiss { get; init; }

    [RelayCommand]
    private void Dismiss() => OnDismiss?.Invoke(this);

    /// <summary>使用者按下 action 按鈕（例：「查看交易」）— 執行 action 並關閉 snackbar。</summary>
    [RelayCommand]
    private void InvokeAction()
    {
        _onAction?.Invoke();
        OnDismiss?.Invoke(this);
    }

    public SnackbarItemViewModel(string text, SnackbarKind kind, string? actionLabel = null, Action? onAction = null)
    {
        Text = text;
        Kind = kind;
        ActionLabel = actionLabel;
        _onAction = onAction;
    }
}
