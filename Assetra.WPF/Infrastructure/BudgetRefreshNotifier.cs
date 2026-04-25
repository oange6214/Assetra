namespace Assetra.WPF.Infrastructure;

public sealed class BudgetRefreshNotifier : IBudgetRefreshNotifier
{
    public event EventHandler? BudgetChanged;

    public void NotifyChanged() => BudgetChanged?.Invoke(this, EventArgs.Empty);
}
