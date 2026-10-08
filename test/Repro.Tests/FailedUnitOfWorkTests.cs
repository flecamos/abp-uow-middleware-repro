using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Repro.Tests;

public class FailedUnitOfWorkTests
{
    private const int ExistingCustomerId = 1;
    private const int MissingCustomerId = 2;

    public static TheoryData<string> AllPipelines =>
        [Pipelines.Template, Pipelines.TemplateWithCompleteOnResponseStarting, Pipelines.WithoutUnitOfWorkMiddleware];

    [Theory]
    [MemberData(nameof(AllPipelines))]
    public async Task A_successful_request_stores_the_order(string pipeline)
    {
        // Arrange
        await using var factory = CreateFactory(pipeline);
        var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("api/orders", new { customerId = ExistingCustomerId });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await client.GetFromJsonAsync<long>("api/orders/count"));
    }

    [Theory]
    [MemberData(nameof(AllPipelines))]
    public async Task A_failing_commit_is_not_reported_as_success(string pipeline)
    {
        // Arrange
        await using var factory = CreateFactory(pipeline);
        var client = factory.CreateClient();

        // Act
        var status = await PostAndReadStatusAsync(client, "api/orders", MissingCustomerId);

        // Assert
        Assert.Equal(0, await client.GetFromJsonAsync<long>("api/orders/count"));
        Assert.Equal(HttpStatusCode.InternalServerError, status);
    }

    [Theory]
    [MemberData(nameof(AllPipelines))]
    public async Task A_failing_event_handler_is_not_reported_as_success(string pipeline)
    {
        // Arrange
        await using var factory = CreateFactory(pipeline);
        var client = factory.CreateClient();

        // Act
        var status = await PostAndReadStatusAsync(client, "api/orders/with-event", ExistingCustomerId);

        // Assert
        Assert.Equal(0, await client.GetFromJsonAsync<long>("api/orders/count"));
        Assert.Equal(HttpStatusCode.InternalServerError, status);
    }

    private static WebApplicationFactory<Program> CreateFactory(string pipeline)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("Repro:Pipeline", pipeline)
        );
        factory.UseKestrel(0);
        factory.StartServer();
        return factory;
    }

    private static async Task<HttpStatusCode> PostAndReadStatusAsync(HttpClient client, string url, int customerId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { customerId }),
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        try
        {
            await response.Content.ReadAsStringAsync();
        }
        catch (Exception)
        {
            // The status line already reached the client before the server failed. That status is what a client acts on.
        }

        return response.StatusCode;
    }
}
