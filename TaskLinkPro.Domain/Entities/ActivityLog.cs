namespace TaskLinkPro.Domain.Entities;

public class ActivityLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EntityType { get; set; } = string.Empty; // Project|Task|Comment
    public Guid EntityId { get; set; }
    public string Action { get; set; } = string.Empty; // Created|Updated|Deleted|StatusChanged|Assigned
    public Guid ActorId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string? Snapshot { get; set; } // JSON
}