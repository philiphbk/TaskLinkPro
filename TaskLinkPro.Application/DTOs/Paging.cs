namespace TaskLinkPro.Application.DTOs;

public record PageRequest(int Page = 1, int PageSize = 20, string? Sort = null, string? Search = null);
public record PageResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
