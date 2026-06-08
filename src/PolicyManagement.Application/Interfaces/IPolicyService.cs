namespace PolicyManagement.Application.Interfaces;

using PolicyManagement.Application.DTOs;

public interface IPolicyService
{
    Task<PaginatedResult<PolicyListItemDto>> ListAsync(PolicyListQuery query, CancellationToken ct);
    Task<PolicyDetailDto?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<BulkFlagResultDto> BulkFlagAsync(IReadOnlyList<Guid> ids, CancellationToken ct);
    Task<PolicySummaryDto> GetSummaryAsync(CancellationToken ct);
}
