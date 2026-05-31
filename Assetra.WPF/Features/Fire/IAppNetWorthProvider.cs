namespace Assetra.WPF.Features.Fire;

public interface IAppNetWorthProvider
{
    Task<decimal> GetCurrentNetWorthAsync(CancellationToken ct = default);
}
