namespace PolicyManagement.Application.DTOs;

public sealed record PolicyListItemDto(
    Guid Id,
    string PolicyNumber,
    string PolicyholderName,
    string LineOfBusiness,
    string Status,
    decimal PremiumAmount,
    string Currency,
    DateTime EffectiveDate,
    DateTime ExpiryDate,
    string Region,
    string Underwriter,
    bool FlaggedForReview
);
