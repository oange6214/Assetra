namespace Assetra.WPF.Infrastructure;

public interface IBudgetRefreshNotifier
{
    event EventHandler? BudgetChanged;

    void NotifyChanged();
}
