using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;

namespace Assetra.Core.Models;

public enum AlertCondition { Above, Below }

/// <summary>
/// 警示規則。Sync-Status-Indicator 補洞：補上 <see cref="EntityVersion"/> 讓 Alert
/// 加入雲端同步管線，跟 RealEstate / Retirement / PhysicalAsset 等其他 leaf entity 對齊。
/// 既有 callsite（VM / tests）大多不關心 sync 語意，所以 Version 給預設值 = <c>new()</c>
/// （Version=0 / UnixEpoch / 空字串，等同「從未同步」），repo 會在 add/update 時 stamp。
/// </summary>
public sealed record AlertRule(
    Guid Id,
    string Symbol,
    string Exchange,
    AlertCondition Condition,
    decimal TargetPrice,
    bool IsTriggered = false,
    DateTimeOffset? TriggerTime = null) : IVersionedEntity
{
    public EntityVersion Version { get; init; } = new();
    Guid IVersionedEntity.EntityId => Id;
}
