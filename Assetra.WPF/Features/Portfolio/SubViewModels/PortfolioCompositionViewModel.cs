using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

public sealed partial class PortfolioCompositionViewModel : ObservableObject
{
    [ObservableProperty] private decimal _stockValue;
    [ObservableProperty] private decimal _etfValue;
    [ObservableProperty] private double _stockPercent;
    [ObservableProperty] private double _etfPercent;
    [ObservableProperty] private bool _hasData;

    /// <summary>
    /// IsEtf is the EFFECTIVE flag — caller resolves it as
    /// row.AssetType == AssetType.Etf || row.IsEtf (ETFs are stored Stock+IsEtf;
    /// see PortfolioViewModel.Filtering.FilterPosition's asset-type predicate).
    /// </summary>
    public void Apply(IReadOnlyList<(bool IsEtf, decimal MarketValueBase)> holdings)
    {
        decimal etf = 0m, stock = 0m;
        foreach (var (isEtf, mv) in holdings)
        {
            if (mv <= 0m) continue;
            if (isEtf) etf += mv; else stock += mv;
        }
        var total = etf + stock;
        StockValue = stock; EtfValue = etf;
        StockPercent = total > 0m ? (double)(stock / total) * 100d : 0d;
        EtfPercent = total > 0m ? (double)(etf / total) * 100d : 0d;
        HasData = total > 0m;
    }
}
