using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TaskLinkPro.Application.DTOs;
using Xunit;

namespace TaskLinkPro.Tests;

public class TasksEndpointsTests : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client;

    public TasksEndpointsTests(TestWebAppFactory factory)
        => _client = factory.CreateClient();

    [Fact]
    public async Task Create_List_Update_Delete_Task_HappyPath()
    {
        // Create a project first
        var proj = await _client.PostAsJsonAsync("/projects", new CreateProjectRequest("Proj A", null, Guid.NewGuid()));
        proj.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdProj = await proj.Content.ReadFromJsonAsync<ProjectResponse>();

        // Create task
        var tCreate = await _client.PostAsJsonAsync($"/projects/{createdProj!.Id}/tasks",
            new CreateTaskRequest("Task 1", "desc", null));
        tCreate.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdTask = await tCreate.Content.ReadFromJsonAsync<TaskResponse>();

        // List
        var tList = await _client.GetAsync($"/projects/{createdProj.Id}/tasks");
        tList.StatusCode.Should().Be(HttpStatusCode.OK);

        // Get ETag and update
        var tGet = await _client.GetAsync($"/projects/{createdProj.Id}/tasks");
        var page = await tGet.Content.ReadFromJsonAsync<PageResult<TaskResponse>>();
        var etag = page!.Items.First().ETag;

        var upd = await _client.PutAsJsonAsync($"/projects/{createdProj.Id}/tasks/{createdTask!.Id}",
            new UpdateTaskRequest("Task 1 updated", "desc2", null,
                TaskLinkPro.Domain.Entities.TaskStatus.InProgress,
                TaskLinkPro.Domain.Entities.TaskPriority.High, null, etag));
        upd.StatusCode.Should().Be(HttpStatusCode.OK);

        // Delete
        var del = await _client.DeleteAsync($"/projects/{createdProj.Id}/tasks/{createdTask.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
