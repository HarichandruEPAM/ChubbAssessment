using System.Text.Json.Serialization;

namespace PolicyManagement.Domain.Enums;

public enum LineOfBusiness
{
    Property,
    Casualty,
    [JsonPropertyName("A&H")]
    AandH,
    Marine
}
