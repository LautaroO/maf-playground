using System.Net;
using System.Net.Http.Json;
using MafPlayground.AI;
using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Context;
using MafPlayground.AI.Contracts;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Http;
using MafPlayground.Studio.Execution;
using MafPlayground.AI.Runtime.Workflows;
using MafPlayground.AI.Runtime.Agents;
using MafPlayground.AI.Runtime.Registry;
using Microsoft.Agents.AI.Workflows;
using MafPlayground.Studio.Tests.Component;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Net.Sockets;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;

namespace MafPlayground.Studio.Tests.Fixtures;

internal sealed class StudioTransportHost(WebApplication app, HttpClient http) : IAsyncDisposable
{
    internal WebApplication App => app;
    internal HttpClient Http => http;
    internal StudioAccessPolicy Policy => app.Services.GetRequiredService<StudioAccessPolicy>();
    internal StudioExecutionFixture.RecordingClient Model => app.Services.GetRequiredService<StudioExecutionFixture.RecordingClient>();
    internal static async Task<StudioTransportHost> Create(bool loopback = false, int maxCompleted = 100, int maxPayload = 2 * 1024 * 1024, int maxRequests = 10000, string? defaultModel = "fake:model", string? alternateModel = null, string[]? additionalIds = null, string? opaqueTransportId = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        if (loopback)
        {
            var reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop();
            string address = $"http://127.0.0.1:{port}";
            builder.WebHost.UseUrls(address);
            builder.Configuration["Studio:Url"] = address;
        }
        else builder.WebHost.UseTestServer();
        builder.Configuration["AI:Defaults:Model"] = defaultModel;
        builder.Services.AddSingleton<StudioExecutionFixture.RecordingClient>();
        builder.Services.AddSingleton<IChatClientProvider>(p => new StudioExecutionFixture.ClientProvider(p.GetRequiredService<StudioExecutionFixture.RecordingClient>()));
        builder.Services.AddSingleton<IUserContextAccessor, EmptyUser>();
        builder.Services.AddInMemoryAgentSessions();
        var entities = builder.Services.AddAIRuntime(builder.Configuration, AIStartupValidationMode.LocalInspection)
            .AddAgent<ReviewEntity, ReviewRequest, ReviewResult>("novel-review", entity => entity.WithDescription("Review generic items").AllowAnonymousAccess())
            .AddWorkflow<ReviewEntity, ReviewRequest, ReviewResult>("novel-native", entity => entity.WithDescription("Generic graph").AllowAnonymousAccess()
                .WithMafWorkflowSource((_, context) => new NativeSource(context.ProfileId)))
            .AddMafPlaygroundSampleEntities();
        foreach (string id in additionalIds ?? [])
            entities.AddWorkflow<ReviewEntity, ReviewRequest, ReviewResult>(id, entity => entity.WithDescription("Generic identifier fixture").AllowAnonymousAccess()
                .WithMafWorkflowSource((_, context) => new NativeSource(context.ProfileId)));
        builder.Services.AddSingleton(new StudioExecutionProfile("alternate", alternateModel is null ? null : MafPlayground.AI.Configuration.AIModelSelection.Parse(alternateModel)));
        builder.Configuration["Studio:Limits:MaxPayloadBytes"] = maxPayload.ToString(System.Globalization.CultureInfo.InvariantCulture);
        builder.Configuration["Studio:Limits:MaxCompletedRuns"] = maxCompleted.ToString(System.Globalization.CultureInfo.InvariantCulture);
        builder.Configuration["Studio:Limits:MaxRequestRecords"] = maxRequests.ToString(System.Globalization.CultureInfo.InvariantCulture);
        builder.Services.AddStudio(builder.Configuration);
        // This transport-only adapter exercises opaque query decoding. AIBuilder intentionally
        // accepts only letters, digits, hyphens, underscores and periods for real registrations.
        if (opaqueTransportId is not null)
            builder.Services.AddScoped<IAIEntityInspection>(provider => new OpaqueTransportInspection(
                (IAIEntityInspection)provider.GetRequiredService<IAIEntityRegistry>(), opaqueTransportId));
        var app = builder.Build();
        if (!loopback) app.Use(async (context, next) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; await next(); });
        app.UseStudioAccess();
        app.MapStudio();
        await app.StartAsync();
        HttpClient http = loopback ? new() : app.GetTestClient();
        http.BaseAddress = app.Services.GetRequiredService<StudioAccessPolicy>().Endpoint;
        http.DefaultRequestHeaders.Authorization = new("Bearer", app.Services.GetRequiredService<StudioAccessPolicy>().BearerToken);
        return new(app, http);
    }
    internal Task<McpClient> Connect(CancellationToken token = default) => McpClient.CreateAsync(new HttpClientTransport(new()
    { Endpoint = new(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp }, http, ownsHttpClient: false), cancellationToken: token);
    internal async Task<StudioRunSnapshot> Terminal(string id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var snapshot = (await http.GetFromJsonAsync<StudioRunSnapshot>(StudioEndpoints.Prefix + "/runs/" + id, timeout.Token))!;
            if (snapshot.IsTerminal) return snapshot;
            await Task.Delay(10, timeout.Token);
        }
    }
    public async ValueTask DisposeAsync() { http.Dispose(); await app.StopAsync(); await app.DisposeAsync(); }
    private sealed class NativeSource(string profile) : IMafWorkflowSource
    {
        public Workflow CreateWorkflow()
        {
            var start = new NeverRunExecutor("start");
            var finish = new NeverRunExecutor("finish-" + profile);
            return new WorkflowBuilder(start).AddEdge(start, finish).WithOutputFrom(finish).Build();
        }
        public Workflow CreateWorkflowForDevUI(string workflowName) => throw new NotSupportedException();
    }
    private sealed class OpaqueTransportInspection(IAIEntityInspection inner, string opaqueId) : IAIEntityInspection
    {
        private string Source(string id) => id == opaqueId ? "novel-native" : id;
        public AIEntityRegistrationInfo GetRegistration(string entityId)
        {
            var registration = inner.GetRegistration(Source(entityId));
            return entityId == opaqueId ? registration with { Descriptor = registration.Descriptor with { Id = entityId } } : registration;
        }
        public AIEntityAvailability GetAvailability(string entityId) => inner.GetAvailability(Source(entityId));
        public IMafAgentSource? GetAgentSource(string entityId, AIEntityInspectionContext context) => inner.GetAgentSource(Source(entityId), context);
        public IMafWorkflowSource? GetWorkflowSource(string entityId, AIEntityInspectionContext context) => inner.GetWorkflowSource(Source(entityId), context);
    }
    private sealed class EmptyUser : IUserContextAccessor { public UserContext GetCurrent() => new([]); }
}
