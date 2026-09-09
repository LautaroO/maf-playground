using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using MafPlayground.AI;
using MafPlayground.AI.Abstractions;
using MafPlayground.AI.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Tests.AI.Foundation;

public sealed class AIEndpointTests
{
    [Fact]
    public async Task Endpoint_EnforcesDiscoveryCallerPolicyAndSessionRules()
    {
        await using WebApplication app = await CreateApplicationAsync();
        string address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using HttpClient client = new() { BaseAddress = new Uri(address) };

        HttpResponseMessage unknown = await client.PostAsJsonAsync(
            "/api/ai/entities/missing/runs",
            new { input = new { value = "test" } });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        HttpResponseMessage anonymous = await client.PostAsJsonAsync(
            "/api/ai/entities/public-echo/runs",
            new { input = new { value = "public" } });
        Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);

        HttpResponseMessage unauthenticated = await client.PostAsJsonAsync(
            "/api/ai/entities/echo/runs",
            new { input = new { value = "test" } });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        using HttpRequestMessage invalidJson = new(
            HttpMethod.Post,
            "/api/ai/entities/echo/runs")
        {
            Content = new StringContent("{invalid", System.Text.Encoding.UTF8, "application/json"),
        };
        invalidJson.Headers.Add("X-Test-Subject", "subject-1");
        invalidJson.Headers.Add("X-Test-Allow", "true");
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.SendAsync(invalidJson)).StatusCode);

        using HttpRequestMessage forbiddenRequest = CreateRequest("test", allow: false);
        HttpResponseMessage forbidden = await client.SendAsync(forbiddenRequest);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using HttpRequestMessage validRequest = CreateRequest("hello", allow: true);
        HttpResponseMessage valid = await client.SendAsync(validRequest);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        string body = await valid.Content.ReadAsStringAsync();
        Assert.Contains("\"entityId\":\"echo\"", body, StringComparison.Ordinal);
        Assert.Contains("\"value\":\"hello\"", body, StringComparison.Ordinal);

        using HttpRequestMessage failingRequest = CreateRequest("fail", allow: true);
        HttpResponseMessage failing = await client.SendAsync(failingRequest);
        Assert.Equal(HttpStatusCode.InternalServerError, failing.StatusCode);
        string failureBody = await failing.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sensitive implementation detail", failureBody, StringComparison.Ordinal);
        Assert.Contains("executionId", failureBody, StringComparison.Ordinal);

        await AssertMappedFailureAsync(client, "budget", HttpStatusCode.TooManyRequests);
        await AssertMappedFailureAsync(client, "provider", HttpStatusCode.BadGateway);
        await AssertMappedFailureAsync(client, "timeout", HttpStatusCode.GatewayTimeout);

        using HttpRequestMessage oversized = CreateRequest(new string('x', 300), allow: true);
        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            (await client.SendAsync(oversized)).StatusCode);

        using HttpRequestMessage sessionRequest = CreateRequest("hello", allow: true, "session");
        HttpResponseMessage session = await client.SendAsync(sessionRequest);
        Assert.Equal(HttpStatusCode.BadRequest, session.StatusCode);
    }

    private static HttpRequestMessage CreateRequest(
        string value,
        bool allow,
        string? sessionId = null)
    {
        HttpRequestMessage request = new(
            HttpMethod.Post,
            "/api/ai/entities/echo/runs")
        {
            Content = JsonContent.Create(new { input = new { value }, sessionId }),
        };
        request.Headers.Add("X-Test-Subject", "subject-1");
        if (allow)
        {
            request.Headers.Add("X-Test-Allow", "true");
        }
        return request;
    }

    private static async Task AssertMappedFailureAsync(
        HttpClient client,
        string value,
        HttpStatusCode expected)
    {
        using HttpRequestMessage request = CreateRequest(value, allow: true);
        HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }

    private static async Task<WebApplication> CreateApplicationAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options =>
            options.Listen(IPAddress.Loopback, 0));
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Defaults:Model"] = "fake:model",
                ["AI:Http:MaxRequestBodyBytes"] = "256",
            })
            .Build();
        builder.Services.AddSingleton<IChatClientProvider>(
            new FakeProvider(new FakeChatClient("unused")));
        builder.Services.AddAIRuntime(configuration)
            .AddWorkflow<EchoEntity, EchoInput, EchoOutput>(
                "echo",
                entity => entity
                    .WithDescription("Echo.")
                    .RequireAuthorization("AI.Echo"))
            .AddWorkflow<AnonymousEchoEntity, EchoInput, EchoOutput>(
                "public-echo",
                entity => entity
                    .WithDescription("Public echo.")
                    .AllowAnonymousAccess());
        builder.Services.AddAIEndpoints(configuration);
        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
            options.AddPolicy("AI.Echo", policy => policy.RequireClaim("allow", "true"));
        });

        WebApplication app = builder.Build();
        app.Use(async (context, next) =>
        {
            string subject = context.Request.Headers["X-Test-Subject"].ToString();
            if (!string.IsNullOrWhiteSpace(subject))
            {
                List<Claim> claims = [new("sub", subject)];
                if (context.Request.Headers["X-Test-Allow"] == "true")
                {
                    claims.Add(new Claim("allow", "true"));
                }
                context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
            }
            await next(context);
        });
        app.UseAuthorization();
        app.MapAIEntities();
        await app.StartAsync();
        return app;
    }

    private sealed record EchoInput(string Value);

    private sealed record EchoOutput(string Value);

    private sealed class EchoEntity : IAIEntity<EchoInput, EchoOutput>
    {
        public ValueTask<EchoOutput> ExecuteAsync(
            EchoInput input,
            AIExecutionContext context,
            CancellationToken cancellationToken) => input.Value switch
            {
                "fail" => throw new InvalidOperationException("sensitive implementation detail"),
                "budget" => throw new MafPlayground.AI.Guards.Budget.BudgetExceededException(
                    "model_calls",
                    2,
                    1),
                "provider" => throw new AIProviderFailureException("provider failed"),
                "timeout" => throw new TimeoutException("provider detail"),
                _ => ValueTask.FromResult(new EchoOutput(input.Value)),
            };
    }

    private sealed class AnonymousEchoEntity : IAIEntity<EchoInput, EchoOutput>
    {
        public ValueTask<EchoOutput> ExecuteAsync(
            EchoInput input,
            AIExecutionContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new EchoOutput(input.Value));
    }

    private sealed class FakeProvider(IChatClient client) : IChatClientProvider
    {
        public string Name => "fake";

        public IChatClient CreateChatClient(string model) => client;
    }
}
