namespace Assetra.Core.Models;

/// <summary>
/// Discriminated mutation passed to <see cref="Interfaces.ITradeRepository.ApplyAtomicAsync"/>
/// so a batch of trade additions/deletions can be wrapped in one SQLite transaction.
/// </summary>
public abstract record TradeMutation;

public sealed record AddTradeMutation(Trade Trade) : TradeMutation;

public sealed record RemoveTradeMutation(Guid Id) : TradeMutation;
