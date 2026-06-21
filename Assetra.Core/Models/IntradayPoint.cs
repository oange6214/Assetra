namespace Assetra.Core.Models;

/// <summary>
/// 盤中分時點：時間（含時分）＋ 收盤價。給績效比較頁 1D/5D 盤中曲線用——日資料的
/// <see cref="OhlcvPoint"/> 是 <c>DateOnly</c>，撐不住盤中時間戳，故另立模型。
/// </summary>
public sealed record IntradayPoint(DateTime At, decimal Close);
