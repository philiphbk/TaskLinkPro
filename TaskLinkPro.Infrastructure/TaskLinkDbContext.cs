using Microsoft.EntityFrameworkCore;
using TaskLinkPro.Domain.Entities;


namespace TaskLinkPro.Infrastructure;

public class TaskLinkDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    public TaskLinkDbContext(DbContextOptions<TaskLinkDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();
        b.Entity<Project>()
            .Property(p => p.RowVersion)
            .IsRowVersion();
        b.Entity<TaskItem>()
            .Property(t => t.RowVersion)
            .IsRowVersion();
        // relationships
        b.Entity<Project>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<TaskItem>()
            .HasOne<Project>()
            .WithMany()
            .HasForeignKey(t => t.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
