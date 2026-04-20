namespace Assetra.Core.Models;

public enum AlertCondition { Above, Below }

public sealed record AlertRule(
    Guid Id,
    string Symbol,
    string Exchange,
    AlertCondition Condition,
    decimal TargetPrice,
    bool IsTriggered = false,
    DateTimeOffset? TriggerTime = null);
