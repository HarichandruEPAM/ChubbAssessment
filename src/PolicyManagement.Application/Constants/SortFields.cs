namespace PolicyManagement.Application.Constants;

public static class SortFields
{
    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "policyNumber",
        "policyholderName",
        "status",
        "lineOfBusiness",
        "premiumAmount",
        "effectiveDate",
        "expiryDate",
        "createdAt",
        "updatedAt"
    };

    public const string Default = "createdAt";
}
