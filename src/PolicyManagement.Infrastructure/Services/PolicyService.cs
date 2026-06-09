using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PolicyManagement.Application.Constants;
using PolicyManagement.Application.DTOs;
using PolicyManagement.Application.Interfaces;
using PolicyManagement.Domain.Entities;
using PolicyManagement.Domain.Enums;
using PolicyManagement.Infrastructure.Data;

namespace PolicyManagement.Infrastructure.Services;

public class PolicyService : IPolicyService
{
    private readonly PolicyDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly int _expiringSoonDays;

    // Keys are camelCase to match API sort parameter convention; OrdinalIgnoreCase comparer handles variant casing.
    // Allow-list for sort fields (ADR-009 / RISK-04). Keys must match SortFields.Allowed in Application — that is the single source of truth for allowed names.
    private static readonly Dictionary<string, Expression<Func<Policy, object>>> SortSelectors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["policyNumber"]      = p => p.PolicyNumber,
        ["policyholderName"]  = p => p.PolicyholderName,
        ["status"]            = p => p.Status,
        ["lineOfBusiness"]    = p => p.LineOfBusiness,
        ["premiumAmount"]     = p => p.PremiumAmount,
        ["effectiveDate"]     = p => p.EffectiveDate,
        ["expiryDate"]        = p => p.ExpiryDate,
        ["createdAt"]         = p => p.CreatedAt,
        ["updatedAt"]         = p => p.UpdatedAt,
    };

    public PolicyService(PolicyDbContext db, TimeProvider timeProvider, IConfiguration configuration)
    {
        _db = db;
        _timeProvider = timeProvider;
        _expiringSoonDays = configuration.GetValue<int>("PolicySummary:ExpiringSoonThresholdDays", 30);
    }

    public async Task<PaginatedResult<PolicyListItemDto>> ListAsync(PolicyListQuery query, CancellationToken ct)
    {
        var q = _db.Policies.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Status) &&
            Enum.TryParse<PolicyStatus>(query.Status, ignoreCase: true, out var status))
            q = q.Where(p => p.Status == status);

        if (!string.IsNullOrWhiteSpace(query.LineOfBusiness) &&
            Enum.TryParse<LineOfBusiness>(query.LineOfBusiness, ignoreCase: true, out var lob))
            q = q.Where(p => p.LineOfBusiness == lob);

        if (!string.IsNullOrWhiteSpace(query.Region))
            q = q.Where(p => p.Region == query.Region);

        if (query.EffectiveDateFrom.HasValue)
            q = q.Where(p => p.EffectiveDate >= query.EffectiveDateFrom.Value);

        if (query.EffectiveDateTo.HasValue)
            q = q.Where(p => p.EffectiveDate <= query.EffectiveDateTo.Value);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search}%";
            q = q.Where(p =>
                EF.Functions.Like(p.PolicyNumber, pattern) ||
                EF.Functions.Like(p.PolicyholderName, pattern) ||
                EF.Functions.Like(p.Underwriter, pattern));
        }

        var totalCount = await q.CountAsync(ct);

        // Unknown sort field: fall back to SortFields.Default desc. The allow-list is validated at the API layer (Phase 5 Task 5.3).
        var selector = SortSelectors.GetValueOrDefault(query.Sort ?? SortFields.Default)
                       ?? SortSelectors[SortFields.Default];
        q = (query.SortDirection ?? "desc").Equals("asc", StringComparison.OrdinalIgnoreCase)
            ? q.OrderBy(selector)
            : q.OrderByDescending(selector);

        var items = await q
            .Skip((query.Page - 1) * query.Size)
            .Take(query.Size)
            .Select(p => new PolicyListItemDto(
                p.Id, p.PolicyNumber, p.PolicyholderName,
                p.LineOfBusiness.ToString(), p.Status.ToString(),
                p.PremiumAmount, p.Currency, p.EffectiveDate, p.ExpiryDate,
                p.Region, p.Underwriter, p.FlaggedForReview))
            .ToListAsync(ct);

        return new PaginatedResult<PolicyListItemDto>(items, totalCount, query.Page, query.Size);
    }

    public async Task<PolicyDetailDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var p = await _db.Policies.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return null;

        return new PolicyDetailDto(
            p.Id, p.PolicyNumber, p.PolicyholderName,
            p.LineOfBusiness.ToString(), p.Status.ToString(),
            p.PremiumAmount, p.Currency, p.EffectiveDate, p.ExpiryDate,
            p.Region, p.Underwriter, p.FlaggedForReview);
    }

    public async Task<BulkFlagResultDto> BulkFlagAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var flagged = await _db.Policies
            .Where(p => ids.Contains(p.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.FlaggedForReview, true)
                .SetProperty(p => p.UpdatedAt, now),
                ct);

        return new BulkFlagResultDto(flagged, ids.Count - flagged);
    }

    public async Task<PolicySummaryDto> GetSummaryAsync(CancellationToken ct)
    {
        var statusCounts = await _db.Policies
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status.ToString(), x => x.Count, ct);

        var premiumByLob = await _db.Policies
            .GroupBy(p => p.LineOfBusiness)
            .Select(g => new { Lob = g.Key, Total = g.Sum(p => p.PremiumAmount) })
            .ToDictionaryAsync(x => x.Lob.ToString(), x => x.Total, ct);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var cutoff = now.AddDays(_expiringSoonDays);
        var expiringSoon = await _db.Policies
            .CountAsync(p => p.Status == PolicyStatus.Active
                          && p.ExpiryDate >= now
                          && p.ExpiryDate <= cutoff, ct);

        return new PolicySummaryDto(statusCounts, premiumByLob, expiringSoon);
    }
}
