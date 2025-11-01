namespace TaskLinkPro.Application.DTOs;

public record ProjectResponse(Guid Id, string Name, string? Description,
    Guid OwnerId, DateTime CreatedAt, DateTime UpdatedAt, string ETag);

public record CreateProjectRequest(string Name, string? Description, Guid OwnerId);

public record UpdateProjectRequest(string Name, string? Description, string IfMatch);
