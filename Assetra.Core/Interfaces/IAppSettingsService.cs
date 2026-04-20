using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IAppSettingsService
{
    AppSettings Current { get; }
    Task SaveAsync(AppSettings settings);

    /// <summary>
    /// 設定成功儲存後觸發。ViewModel 可訂閱以即時重算依賴於設定值的顯示
    /// （如手續費折扣、貨幣、目標配置等）。
    /// </summary>
    event Action? Changed;
}
