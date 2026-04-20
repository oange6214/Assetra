using Microsoft.Extensions.Hosting;
using Assetra.Core.Interfaces;

namespace Assetra.WPF.Infrastructure;

internal sealed class MarketDataHostedService(IStockService stockService) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        stockService.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        try { stockService.Stop(); }
        catch (Exception ex) { Serilog.Log.Warning(ex, "StockService stop failed"); }

        return Task.CompletedTask;
    }
}
