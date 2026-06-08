namespace PolicyManagement.Application.DTOs;

public sealed record PaginatedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int Size
);
