using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TaskLinkPro.Application.DTOs;
using Xunit;

namespace TaskLinkPro.Tests;

public class ProjectsEndpointsTests : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client;

    public ProjectsEndpointsTests(TestWebAppFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task List_Empty_ReturnsEmptyPage()
    {
        var res = await _client.GetAsync("/projects");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await res.Content.ReadFromJsonAsync<PageResult<ProjectResponse>>();
        page.Should().NotBeNull();
        page!.Items.Should().BeEmpty();
        page.Page.Should().Be(1);
    }

    [Fact]
    public async Task Create_Then_Get_Then_Update_With_ETag_Works()
    {
        // Create
        var create = new CreateProjectRequest("Phase2 Project", "Init desc", Guid.NewGuid());
        var createRes = await _client.PostAsJsonAsync("/projects", create);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await createRes.Content.ReadFromJsonAsync<ProjectResponse>();
        created.Should().NotBeNull();
        var id = created!.Id;

        // Get and capture ETag
        var getRes = await _client.GetAsync($"/projects/{id}");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var current = await getRes.Content.ReadFromJsonAsync<ProjectResponse>();
        var eTag = getRes.Headers.ETag?.Tag ?? current!.ETag;
        eTag.Should().NotBeNull();

        // Update with If-Match
        var update = new UpdateProjectRequest("Phase2 Project UPDATED", "New desc", eTag!);
        var updRes = await _client.PutAsJsonAsync($"/projects/{id}", update);
        updRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updRes.Content.ReadFromJsonAsync<ProjectResponse>();
        updated!.Name.Should().Be("Phase2 Project UPDATED");

        // Stale ETag should fail with 412
        var staleUpd = await _client.PutAsJsonAsync($"/projects/{id}",
            new UpdateProjectRequest("Again", "Again", eTag!));
        staleUpd.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task Delete_Works()
    {
        var create = new CreateProjectRequest("Delete Me", null, Guid.NewGuid());
        var resCreate = await _client.PostAsJsonAsync("/projects", create);
        var created = await resCreate.Content.ReadFromJsonAsync<ProjectResponse>();

        var del = await _client.DeleteAsync($"/projects/{created!.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await _client.GetAsync($"/projects/{created.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
