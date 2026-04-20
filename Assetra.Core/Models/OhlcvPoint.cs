namespace Assetra.Core.Models;

public record OhlcvPoint(DateOnly Date, decimal Open, decimal High, decimal Low, decimal Close, long Volume);
