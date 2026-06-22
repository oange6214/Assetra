using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Assetra.WPF.Features.Portfolio;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using SkiaSharp;

namespace Assetra.WPF.Features.Trends;

public partial class TrendsView : UserControl
{
    // 十字準星只在 hover 時顯示：VM 不設 CrosshairPaint（一設就會持續殘留在預設位置），改由這裡在 MouseMove
    // 掛上、MouseLeave 清掉。固定中性灰（兩主題皆可讀）＋ 虛線。
    private SolidColorPaint? _crosshairPaint;
    private SolidColorPaint CrosshairPaint => _crosshairPaint ??= new SolidColorPaint(new SKColor(0x78, 0x7B, 0x86))
    {
        StrokeThickness = 1,
        PathEffect = new DashEffect([4f, 4f]),
    };

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

    /// <summary>
    /// 滑鼠在比較圖上移動 → (1) 掛上十字準星線（離開會清掉，避免殘留）；(2) 把游標 X 換算成日期，設定
    /// VM.ComparisonHoverDate，下方清單即顯示那點的同期 %。LiveCharts2 DateTimeAxis 以 <c>DateTime.Ticks</c>
    /// 當 X 座標（盤中為壓縮後的合成時間）；取值失敗時靜默忽略（清單退回顯示期末）。
    /// </summary>
    private void CompareChart_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (DataContext is not PortfolioHistoryViewModel vm
            || sender is not LiveChartsCore.SkiaSharpView.WPF.CartesianChart chart)
            return;

        if (chart.XAxes?.FirstOrDefault() is Axis ax && ax.CrosshairPaint is null)
            ax.CrosshairPaint = CrosshairPaint;

        try
        {
            var pos = e.GetPosition(chart);
            var data = chart.ScalePixelsToData(new LiveChartsCore.Drawing.LvcPointD(pos.X, pos.Y));
            var ticks = (long)data.X;
            if (ticks > System.DateTime.MinValue.Ticks && ticks < System.DateTime.MaxValue.Ticks)
                vm.ComparisonHoverDate = new System.DateTime(ticks);
        }
        catch
        {
            // hover 取值失敗不影響圖表 / 清單
        }
    }

    private void CompareChart_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (DataContext is PortfolioHistoryViewModel vm)
            vm.ComparisonHoverDate = null;

        // 清掉準星線，避免滑鼠離開後還殘留一條虛線。
        if (sender is LiveChartsCore.SkiaSharpView.WPF.CartesianChart chart
            && chart.XAxes?.FirstOrDefault() is Axis ax)
            ax.CrosshairPaint = null;
    }
}
