namespace PolicyManagement.Domain.Enums;

public enum LineOfBusiness
{
    Property,
    Casualty,
    // Wire representation is "AandH" — '&' is not valid in a C# identifier.
    // The OpenAPI spec documents this as the on-the-wire value.
    AandH,
    Marine
}
