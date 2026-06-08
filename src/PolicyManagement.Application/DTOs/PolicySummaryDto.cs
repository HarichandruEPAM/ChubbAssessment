namespace PolicyManagement.Application.DTOs;

public sealed record PolicySummaryDto(
    IReadOnlyDictionary<string, int> StatusCounts,
    IReadOnlyDictionary<string, decimal> TotalPremiumByLineOfBusiness,
    int ExpiringSoonCount
);
