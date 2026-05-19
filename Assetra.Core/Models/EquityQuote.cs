namespace Assetra.Core.Models;

public sealed record EquityQuote
{
    public EquityQuote(
        EquityInstrumentKey instrument,
        decimal price,
        decimal? previousClose,
        decimal? change,
        decimal? changePercent,
        string currency,
        DateTimeOffset updatedAt,
        string sourceProvider,
        bool isDelayed,
        string name = "",
        bool isStale = false,
        string providerStateMessage = "")
    {
        ArgumentNullException.ThrowIfNull(instrument);

        if (price < 0)
            throw new ArgumentOutOfRangeException(nameof(price), "Quote price cannot be negative.");
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Quote currency cannot be blank.", nameof(currency));

        Instrument = instrument;
        Price = price;
        PreviousClose = previousClose;
        Change = change;
        ChangePercent = changePercent;
        Currency = currency.Trim().ToUpperInvariant();
        UpdatedAt = updatedAt;
        SourceProvider = sourceProvider?.Trim() ?? string.Empty;
        IsDelayed = isDelayed;
        Name = name?.Trim() ?? string.Empty;
        IsStale = isStale;
        ProviderStateMessage = providerStateMessage?.Trim() ?? string.Empty;
    }

    public EquityInstrumentKey Instrument { get; init; }
    public decimal Price { get; init; }
    public decimal? PreviousClose { get; init; }
    public decimal? Change { get; init; }
    public decimal? ChangePercent { get; init; }
    public string Currency { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string SourceProvider { get; init; }
    public bool IsDelayed { get; init; }
    public string Name { get; init; }
    public bool IsStale { get; init; }
    public string ProviderStateMessage { get; init; }
}
