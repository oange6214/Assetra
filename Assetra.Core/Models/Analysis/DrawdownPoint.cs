namespace Assetra.Core.Models.Analysis;

public sealed record DrawdownPoint(DateOnly Date, decimal Value, decimal Peak, decimal Drawdown);
