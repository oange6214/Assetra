using Assetra.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// Owns the empty-state setup notice shown above the Portfolio tabs.
/// Visibility, title/message/action text, and the primary action all derive
/// from <see cref="HasNoCashAccounts"/> / <see cref="HasNoTrades"/>, which the
/// parent <see cref="PortfolioViewModel"/> pushes in via <see cref="Refresh"/>.
/// The action itself is delegated back to the parent through callbacks so this
/// VM stays decoupled from dialog/load logic.
/// </summary>
public sealed partial class SetupNoticeViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;
    private readonly Action _onAddAccount;
    private readonly Action _onAddTrade;

    public SetupNoticeViewModel(
        ILocalizationService localization,
        Action onAddAccount,
        Action onAddTrade)
    {
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(onAddAccount);
        ArgumentNullException.ThrowIfNull(onAddTrade);
        _localization = localization;
        _onAddAccount = onAddAccount;
        _onAddTrade = onAddTrade;
    }

    [ObservableProperty] private bool _hasNoCashAccounts = true;
    [ObservableProperty] private bool _hasNoTrades = true;

    public bool HasSetupNotice => HasNoCashAccounts || HasNoTrades;

    public string Title =>
        HasNoCashAccounts
            ? L("Portfolio.Setup.NoAccounts.Title")
            : HasNoTrades
                ? L("Portfolio.Setup.NoTrades.Title")
                : string.Empty;

    public string Message =>
        HasNoCashAccounts
            ? L("Portfolio.Setup.NoAccounts.Message")
            : HasNoTrades
                ? L("Portfolio.Setup.NoTrades.Message")
                : string.Empty;

    public string ActionText =>
        HasNoCashAccounts
            ? L("Portfolio.Setup.NoAccounts.Action")
            : HasNoTrades
                ? L("Portfolio.Setup.NoTrades.Action")
                : string.Empty;

    /// <summary>Pushes the latest empty-state flags from the parent and re-raises
    /// the dependent properties.</summary>
    public void Refresh(bool hasNoCashAccounts, bool hasNoTrades)
    {
        HasNoCashAccounts = hasNoCashAccounts;
        HasNoTrades = hasNoTrades;
    }

    partial void OnHasNoCashAccountsChanged(bool _) => RaiseDerived();
    partial void OnHasNoTradesChanged(bool _) => RaiseDerived();

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(HasSetupNotice));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(ActionText));
    }

    [RelayCommand]
    private void Execute()
    {
        if (HasNoCashAccounts)
        {
            _onAddAccount();
            return;
        }

        if (HasNoTrades)
            _onAddTrade();
    }

    private string L(string key) => _localization.Get(key, string.Empty);
}
