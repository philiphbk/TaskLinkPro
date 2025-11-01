
namespace TaskLinkPro.Application.DTOs;

public record CreateTaskRequest(string Title, string? Description, Guid? AssigneeId,
    TaskLinkPro.Domain.Entities.TaskPriority Priority = TaskLinkPro.Domain.Entities.TaskPriority.Medium, DateTime? DueDate = null);

public record UpdateTaskRequest(string Title, string? Description, Guid? AssigneeId,
    TaskLinkPro.Domain.Entities.TaskStatus Status, TaskLinkPro.Domain.Entities.TaskPriority Priority, DateTime? DueDate, string IfMatch);

public record TaskResponse(Guid Id, Guid ProjectId, string Title, string? Description,
    Guid? AssigneeId, TaskLinkPro.Domain.Entities.TaskStatus Status, TaskLinkPro.Domain.Entities.TaskPriority Priority, DateTime? DueDate,
    DateTime CreatedAt, DateTime UpdatedAt, string ETag);

public record CreateCommentRequest(string Body);
public record CommentResponse(Guid Id, Guid TaskId, Guid AuthorId, string Body, DateTime CreatedAt);
