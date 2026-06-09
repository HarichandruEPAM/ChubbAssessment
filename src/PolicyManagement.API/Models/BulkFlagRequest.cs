using System.ComponentModel.DataAnnotations;

namespace PolicyManagement.API.Models;

public class BulkFlagRequest
{
    [Required(ErrorMessage = "policyIds is required.")]
    [MinLength(1, ErrorMessage = "At least one policy ID is required.")]
    public IReadOnlyList<Guid> PolicyIds { get; set; } = [];
}
