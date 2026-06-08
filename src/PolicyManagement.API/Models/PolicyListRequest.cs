using System.ComponentModel.DataAnnotations;
using PolicyManagement.Application.DTOs;

namespace PolicyManagement.API.Models;

public class PolicyListRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Page must be at least 1.")]
    public int Page { get; set; } = 1;

    [Range(1, 100, ErrorMessage = "Size must be between 1 and 100.")]
    public int Size { get; set; } = 10;

    public string? Sort { get; set; } = "createdAt";
    public string? SortDirection { get; set; } = "desc";
    public string? Status { get; set; }
    public string? LineOfBusiness { get; set; }
    public string? Region { get; set; }
    public DateTime? EffectiveDateFrom { get; set; }
    public DateTime? EffectiveDateTo { get; set; }
    public string? Search { get; set; }

    public PolicyListQuery ToQuery() => new(
        Page, Size, Sort ?? "createdAt", SortDirection ?? "desc",
        Status, LineOfBusiness, Region, EffectiveDateFrom, EffectiveDateTo, Search);
}
