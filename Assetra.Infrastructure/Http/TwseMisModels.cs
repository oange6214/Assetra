using System.Text.Json.Serialization;

namespace Assetra.Infrastructure.Http;

internal record TwseMisResponse(
    [property: JsonPropertyName("msgArray")] IReadOnlyList<TwseMisStock>? MsgArray);

internal record TwseMisStock(
    [property: JsonPropertyName("c")] string? Code,
    [property: JsonPropertyName("n")] string? Name,
    [property: JsonPropertyName("z")] string? CurrentPrice,
    [property: JsonPropertyName("y")] string? PrevClose,
    [property: JsonPropertyName("o")] string? Open,
    [property: JsonPropertyName("h")] string? High,
    [property: JsonPropertyName("l")] string? Low,
    [property: JsonPropertyName("v")] string? Volume);

