namespace Assetra.Core.Models.Fire;

public sealed record FireDrawdownProjection(
    IReadOnlyList<FireDrawdownPoint> DrawdownPath,
    IReadOnlyList<FireProjectionWarning> Warnings);
