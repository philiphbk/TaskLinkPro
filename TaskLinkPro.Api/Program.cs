using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using TaskLinkPro.Infrastructure;
using TaskLinkPro.Domain.Entities;
using TaskLinkPro.Api.Authorization;
using TaskLinkPro.Api.Utils;
using TaskLinkPro.Application.DTOs;
using TaskLinkPro.Api.Security;




var builder = WebApplication.CreateBuilder(args);
var secret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(secret))
{
    // dev fallback; replace via user-secrets or env in real deployments
    secret = "dev-secret-change-me-please-32chars-minimum!!";
}

builder.Services.AddDbContext<TaskLinkDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ProjectOwnerOrAdmin", policy =>
        policy.Requirements.Add(new ProjectOwnerOrAdminRequirement()));
});
builder.Services.AddEndpointsApiExplorer().AddSwaggerGen();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "TaskLink Pro API", Version = "v1" });
});
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "global",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
builder.Services.AddCors(o =>
{
    o.AddDefaultPolicy(p => p
        .WithOrigins("http://localhost:3890", "http://localhost:4200")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

app.UseCors();
//app.UseRateLimiter();
app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

var projects = app.MapGroup("/projects")
    .RequireAuthorization(); // adjust policies later if you like
var tasks = app.MapGroup("/projects/{projectId:guid}/tasks").RequireAuthorization();
tasks.MapGet("/", () => Results.Ok(Array.Empty<object>())).WithOpenApi();
tasks.MapPost("/", () => Results.Created("/tasks/1", new { })).WithOpenApi();

var comments = app.MapGroup("/tasks/{taskId:guid}/comments").RequireAuthorization();
comments.MapGet("/", () => Results.Ok(Array.Empty<object>())).WithOpenApi();
comments.MapPost("/", () => Results.Created("/comments/1", new { })).WithOpenApi();



// CREATE
projects.MapPost("/", async (CreateProjectRequest req, TaskLinkDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length < 3 || req.Name.Length > 80)
        return Results.ValidationProblem(new Dictionary<string, string[]> {
            { "Name", new[] {"Name must be 3..80 chars"} }
        });

    var entity = new Project
    {
        Name = req.Name.Trim(),
        Description = req.Description?.Trim(),
        OwnerId = req.OwnerId,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
    db.Projects.Add(entity);
    await db.SaveChangesAsync();

    var eTag = ETagHelper.ToETag(entity.RowVersion ?? Array.Empty<byte>());
    var response = new ProjectResponse(entity.Id, entity.Name, entity.Description, entity.OwnerId,
        entity.CreatedAt, entity.UpdatedAt, eTag);
    return Results.Created($"/projects/{entity.Id}", response);
})
.WithOpenApi();

// LIST (pagination/filter/sort)
projects.MapGet("/", async ([AsParameters] PageRequest q, TaskLinkDbContext db) =>
{
    var page = q.Page <= 0 ? 1 : q.Page;
    var size = q.PageSize <= 0 ? 20 : Math.Min(q.PageSize, 100);

    var query = db.Projects.AsNoTracking();

    if (!string.IsNullOrWhiteSpace(q.Search))
        query = query.Where(p => p.Name.Contains(q.Search));

    query = q.Sort switch
    {
        "-createdAt" => query.OrderByDescending(p => p.CreatedAt),
        "name" => query.OrderBy(p => p.Name),
        _ => query.OrderBy(p => p.CreatedAt)
    };

    var total = await query.CountAsync();
    var items = await query.Skip((page - 1) * size).Take(size)
        .Select(p => new ProjectResponse(
            p.Id, p.Name, p.Description, p.OwnerId, p.CreatedAt, p.UpdatedAt,
            ETagHelper.ToETag(p.RowVersion ?? Array.Empty<byte>())))
        .ToListAsync();

    return Results.Ok(new PageResult<ProjectResponse>(items, page, size, total));
})
.WithOpenApi();

// GET by id
projects.MapGet("/{id:guid}", async (Guid id, TaskLinkDbContext db, HttpContext ctx) =>
{
    var p = await db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
    if (p == null) return Results.NotFound();

    var eTag = ETagHelper.ToETag(p.RowVersion ?? Array.Empty<byte>());
    ctx.Response.Headers.ETag = eTag;
    var res = new ProjectResponse(p.Id, p.Name, p.Description, p.OwnerId, p.CreatedAt, p.UpdatedAt, eTag);
    return Results.Ok(res);
})
.WithOpenApi();

// UPDATE (optimistic concurrency via If-Match)
projects.MapPut("/{id:guid}", async (Guid id, UpdateProjectRequest req, TaskLinkDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length < 3 || req.Name.Length > 80)
        return Results.ValidationProblem(new Dictionary<string, string[]> {
            { "Name", new[] {"Name must be 3..80 chars"} }
        });

    if (!ETagHelper.TryParseIfMatch(req.IfMatch, out var ifMatch))
        return Results.Problem(statusCode: 428, title: "Precondition Required",
            detail: "Missing or invalid If-Match (ETag) header.");

    var p = await db.Projects.FirstOrDefaultAsync(x => x.Id == id);
    if (p == null) return Results.NotFound();

    if (p.RowVersion == null || !p.RowVersion.SequenceEqual(ifMatch!))
        return Results.StatusCode(StatusCodes.Status412PreconditionFailed);

    p.Name = req.Name.Trim();
    p.Description = req.Description?.Trim();
    p.UpdatedAt = DateTime.UtcNow;

    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
    }

    var eTag = ETagHelper.ToETag(p.RowVersion ?? Array.Empty<byte>());
    var res = new ProjectResponse(p.Id, p.Name, p.Description, p.OwnerId, p.CreatedAt, p.UpdatedAt, eTag);
    return Results.Ok(res);
})
.WithOpenApi();

// DELETE
projects.MapDelete("/{id:guid}", async (Guid id, TaskLinkDbContext db) =>
{
    var p = await db.Projects.FirstOrDefaultAsync(x => x.Id == id);
    if (p == null) return Results.NotFound();

    db.Projects.Remove(p);
    await db.SaveChangesAsync();
    return Results.NoContent();
})
.WithOpenApi();

var auth = app.MapGroup("/auth");

auth.MapPost("/register", async (RegisterRequest req, TaskLinkDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password) ||
        string.IsNullOrWhiteSpace(req.FullName))
        return Results.BadRequest(new { message = "Email, Password, FullName are required." });

    var email = req.Email.Trim().ToLowerInvariant();
    if (await db.Users.AsNoTracking().AnyAsync(u => u.Email == email))
        return Results.Conflict(new { message = "Email already registered." });

    var user = new User
    {
        Email = email,
        PasswordHash = PasswordHasher.Hash(req.Password),
        FullName = req.FullName.Trim(),
        Role = "Member",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
    db.Users.Add(user);
    await db.SaveChangesAsync();
    return Results.Created($"/users/{user.Id}", new { user.Id, user.Email, user.FullName, user.Role });
})
.WithOpenApi();

auth.MapPost("/login", async (LoginRequest req, TaskLinkDbContext db, IConfiguration config) =>
{
    var email = (req.Email ?? "").Trim().ToLowerInvariant();
    var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
    if (user == null || !PasswordHasher.Verify(req.Password ?? "", user.PasswordHash))
        return Results.Unauthorized();

    var secret = config["Jwt:Secret"];
    if (string.IsNullOrWhiteSpace(secret))
        secret = "dev-secret-change-me-please-32chars-minimum!!";

    var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(secret));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var expires = DateTime.UtcNow.AddHours(1);

    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.FullName),
        new Claim(ClaimTypes.Email, user.Email),
        new Claim(ClaimTypes.Role, user.Role)
    };

    var token = new JwtSecurityToken(
        claims: claims, expires: expires, signingCredentials: creds);

    var jwt = new JwtSecurityTokenHandler().WriteToken(token);
    return Results.Ok(new AuthResponse(jwt, expires));
})
.WithOpenApi();

app.MapGet("/me", (ClaimsPrincipal user) =>
{
    if (!user.Identity?.IsAuthenticated ?? true) return Results.Unauthorized();
    return Results.Ok(new
    {
        Id = user.FindFirstValue(ClaimTypes.NameIdentifier),
        Name = user.Identity!.Name,
        Email = user.FindFirstValue(ClaimTypes.Email),
        Role = user.FindFirstValue(ClaimTypes.Role)
    });
})
.RequireAuthorization()
.WithOpenApi();


var tasksGroup = app.MapGroup("/projects/{projectId:guid}/tasks").RequireAuthorization();

tasksGroup.MapPost("/", async (Guid projectId, CreateTaskRequest req, TaskLinkDbContext db, ClaimsPrincipal user) =>
{
    var exists = await db.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId);
    if (!exists) return Results.NotFound(new { message = "Project not found." });

    if (string.IsNullOrWhiteSpace(req.Title) || req.Title.Length < 3)
        return Results.ValidationProblem(new Dictionary<string, string[]> { { "Title", new[] { "Title min length 3." } } });

    var t = new TaskItem
    {
        ProjectId = projectId,
        Title = req.Title.Trim(),
        Description = req.Description?.Trim(),
        AssigneeId = req.AssigneeId,
        Priority = req.Priority,
        DueDate = req.DueDate,
        Status = TaskStatus.Todo,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
    db.Tasks.Add(t);
    await db.SaveChangesAsync();

    var resp = new TaskResponse(t.Id, t.ProjectId, t.Title, t.Description, t.AssigneeId, t.Status, t.Priority,
        t.DueDate, t.CreatedAt, t.UpdatedAt, ETagHelper.ToETag(t.RowVersion ?? Array.Empty<byte>()));
    return Results.Created($"/tasks/{t.Id}", resp);
})
.WithOpenApi();

tasksGroup.MapGet("/", async (Guid projectId, [AsParameters] PageRequest q, TaskLinkDbContext db) =>
{
    var page = q.Page <= 0 ? 1 : q.Page;
    var size = q.PageSize <= 0 ? 20 : Math.Min(q.PageSize, 100);

    var query = db.Tasks.AsNoTracking().Where(t => t.ProjectId == projectId);

    if (!string.IsNullOrWhiteSpace(q.Search))
        query = query.Where(t => t.Title.Contains(q.Search));

    query = q.Sort switch
    {
        "-createdAt" => query.OrderByDescending(t => t.CreatedAt),
        "priority"   => query.OrderByDescending(t => t.Priority),
        _            => query.OrderBy(t => t.CreatedAt)
    };

    var total = await query.CountAsync();
    var items = await query.Skip((page - 1) * size).Take(size)
        .Select(t => new TaskResponse(t.Id, t.ProjectId, t.Title, t.Description, t.AssigneeId, t.Status, t.Priority,
            t.DueDate, t.CreatedAt, t.UpdatedAt, ETagHelper.ToETag(t.RowVersion ?? Array.Empty<byte>())))
        .ToListAsync();

    return Results.Ok(new PageResult<TaskResponse>(items, page, size, total));
})
.WithOpenApi();

tasksGroup.MapPut("/{taskId:guid}", async (Guid projectId, Guid taskId, UpdateTaskRequest req, TaskLinkDbContext db, HttpContext ctx) =>
{
    var ifMatchHeader = ctx.Request.Headers["If-Match"].FirstOrDefault();
    var ifMatchValue = string.IsNullOrWhiteSpace(ifMatchHeader) ? req.IfMatch : ifMatchHeader;

    if (!ETagHelper.TryParseIfMatch(ifMatchValue, out var ifMatch))
        return Results.Problem(statusCode: 428, title: "Precondition Required", detail: "Missing/invalid If-Match.");

    var t = await db.Tasks.FirstOrDefaultAsync(x => x.Id == taskId && x.ProjectId == projectId);
    if (t == null) return Results.NotFound();

    if (t.RowVersion == null || !t.RowVersion.SequenceEqual(ifMatch!))
        return Results.StatusCode(StatusCodes.Status412PreconditionFailed);

    if (string.IsNullOrWhiteSpace(req.Title) || req.Title.Length < 3)
        return Results.ValidationProblem(new Dictionary<string, string[]> { { "Title", new[] { "Title min length 3." } } });

    t.Title = req.Title.Trim();
    t.Description = req.Description?.Trim();
    t.AssigneeId = req.AssigneeId;
    t.Status = req.Status;
    t.Priority = req.Priority;
    t.DueDate = req.DueDate;
    t.UpdatedAt = DateTime.UtcNow;

    try { await db.SaveChangesAsync(); }
    catch (DbUpdateConcurrencyException) { return Results.StatusCode(StatusCodes.Status412PreconditionFailed); }

    var resp = new TaskResponse(t.Id, t.ProjectId, t.Title, t.Description, t.AssigneeId, t.Status, t.Priority,
        t.DueDate, t.CreatedAt, t.UpdatedAt, ETagHelper.ToETag(t.RowVersion ?? Array.Empty<byte>()));
    return Results.Ok(resp);
})
.WithOpenApi();

tasksGroup.MapDelete("/{taskId:guid}", async (Guid projectId, Guid taskId, TaskLinkDbContext db) =>
{
    var t = await db.Tasks.FirstOrDefaultAsync(x => x.Id == taskId && x.ProjectId == projectId);
    if (t == null) return Results.NotFound();
    db.Tasks.Remove(t);
    await db.SaveChangesAsync();
    return Results.NoContent();
})
.WithOpenApi();


var commentsGroup = app.MapGroup("/tasks/{taskId:guid}/comments").RequireAuthorization();

commentsGroup.MapPost("/", async (Guid taskId, CreateCommentRequest req, TaskLinkDbContext db, ClaimsPrincipal user) =>
{
    if (string.IsNullOrWhiteSpace(req.Body))
        return Results.ValidationProblem(new Dictionary<string, string[]> { { "Body", new[] { "Body required." } } });

    var taskExists = await db.Tasks.AsNoTracking().AnyAsync(t => t.Id == taskId);
    if (!taskExists) return Results.NotFound(new { message = "Task not found." });

    var authorId = user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (authorId == null) return Results.Unauthorized();

    var c = new Comment
    {
        TaskId = taskId,
        AuthorId = Guid.Parse(authorId),
        Body = req.Body.Trim(),
        CreatedAt = DateTime.UtcNow
    };
    db.Comments.Add(c);
    await db.SaveChangesAsync();

    var resp = new CommentResponse(c.Id, c.TaskId, c.AuthorId, c.Body, c.CreatedAt);
    return Results.Created($"/comments/{c.Id}", resp);
})
.WithOpenApi();

commentsGroup.MapGet("/", async (Guid taskId, [AsParameters] PageRequest q, TaskLinkDbContext db) =>
{
    var page = q.Page <= 0 ? 1 : q.Page;
    var size = q.PageSize <= 0 ? 20 : Math.Min(q.PageSize, 100);

    var query = db.Comments.AsNoTracking().Where(c => c.TaskId == taskId).OrderByDescending(c => c.CreatedAt);
    var total = await query.CountAsync();
    var items = await query.Skip((page - 1) * size).Take(size)
        .Select(c => new CommentResponse(c.Id, c.TaskId, c.AuthorId, c.Body, c.CreatedAt))
        .ToListAsync();

    return Results.Ok(new PageResult<CommentResponse>(items, page, size, total));
})
.WithOpenApi();

commentsGroup.MapDelete("/{commentId:guid}", async (Guid taskId, Guid commentId, TaskLinkDbContext db, ClaimsPrincipal user) =>
{
    var c = await db.Comments.FirstOrDefaultAsync(x => x.Id == commentId && x.TaskId == taskId);
    if (c == null) return Results.NotFound();

    // Only author or admin (simple check)
    var uid = user.FindFirstValue(ClaimTypes.NameIdentifier);
    var role = user.FindFirstValue(ClaimTypes.Role) ?? "";
    if (!(role == "Admin" || (uid != null && Guid.Parse(uid) == c.AuthorId)))
        return Results.Forbid();

    db.Comments.Remove(c);
    await db.SaveChangesAsync();
    return Results.NoContent();
})
.WithOpenApi();



app.Run();
public partial class Program { } // so WebApplicationFactory works in tests
