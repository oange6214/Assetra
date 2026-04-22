using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Dtos;

public sealed record LoanMutationResult(Guid? LiabilityAssetId, IReadOnlyList<LoanScheduleEntry>? ScheduleEntries);
