using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using TaskLinkPro.Infrastructure;

namespace TaskLinkPro.Api.Authorization;
/// <summary>
/// Authorization requirement used to allow only project owners or admins.
/// </summary>
public class ProjectOwnerOrAdminRequirement : IAuthorizationRequirement { }

// Very permissive stub handler so the app compiles and runs.
// TODO: Replace logic with real project-ownership check once endpoints exist.

public class ProjectOwnerOrAdminHandler : AuthorizationHandler<ProjectOwnerOrAdminRequirement>
{
    private readonly TaskLinkDbContext _db;
    /// <summary>DI constructor.</summary>
    public ProjectOwnerOrAdminHandler(TaskLinkDbContext db) => _db = db;

    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ProjectOwnerOrAdminRequirement requirement)
    {
        // TODO: implement real ownership check. Temporary allow:
        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
