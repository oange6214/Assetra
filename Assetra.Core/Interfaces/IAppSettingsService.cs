using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IAppSettingsService
{
    AppSettings Current { get; }

    /// <param name="raiseChanged">
    /// 是否觸發 <see cref="Changed"/>；內部記帳類的持久化（如配額用量）請傳 false，
    /// 避免無意義地驅動全域訂閱者重算／重抓。
    /// </param>
    Task SaveAsync(AppSettings settings, bool raiseChanged = true);

    /// <summary>
    /// 設定成功儲存後觸發。ViewModel 可訂閱以即時重算依賴於設定值的顯示
    /// （如手續費折扣、貨幣、目標配置等）。
    /// </summary>
    event Action? Changed;
}
