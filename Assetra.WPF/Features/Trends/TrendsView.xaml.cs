using System.Windows;
using System.Windows.Controls;
using Assetra.WPF.Features.Portfolio;

namespace Assetra.WPF.Features.Trends;

public partial class TrendsView : UserControl
{
    public TrendsView() => InitializeComponent();

    /// <summary>
    /// View 完全 mount 後重新觸發 ChangePeriodCommand，確保 KPI cards 有正確的
    /// HasKpis 通知。背景：App 初次啟動時 PortfolioViewModel.LoadAsync 鏈會非同步
    /// 跑 History.RefreshChartAsync，這時候 TrendsView 可能還沒 mount 完成，binding
    /// 訂閱錯過第一波 PropertyChanged → KPI cards 不顯示直到使用者手動點 chip 才
    /// 觸發第二次 RefreshChartAsync 才正確顯示。在 Loaded 事件再觸發一次保證綁定
    /// 訂閱完整後 KPIs 一定能正確 propagate。
    /// </summary>
    private void TrendsView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PortfolioHistoryViewModel vm)
            return;
        // ChangePeriodCommand 接受 "30" / "90" / "180" / "365" / "All" 字串。
        // SelectedDays = 0 (AllPeriodDays sentinel) 對應 "All"，其餘對應 number。
        var param = vm.SelectedDays == 0 ? "All" : vm.SelectedDays.ToString();
        if (vm.ChangePeriodCommand.CanExecute(param))
            vm.ChangePeriodCommand.Execute(param);
    }
}
