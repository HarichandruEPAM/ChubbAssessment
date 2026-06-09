using PolicyManagement.Application.Constants;

namespace PolicyManagement.Application.DTOs;

public sealed record PolicyListQuery(
    int Page = 1,
    int Size = 10,
    string Sort = SortFields.Default,
    string SortDirection = "desc",
    string? Status = null,
    string? LineOfBusiness = null,
    string? Region = null,
    DateTime? EffectiveDateFrom = null,
    DateTime? EffectiveDateTo = null,
    string? Search = null
);
