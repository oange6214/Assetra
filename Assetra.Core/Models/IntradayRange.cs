namespace Assetra.Core.Models;

/// <summary>盤中比較的時間範圍：<see cref="OneDay"/>＝今日逐分、<see cref="FiveDays"/>＝近 5 日（較粗時距）。</summary>
public enum IntradayRange
{
    OneDay,
    FiveDays,
}
