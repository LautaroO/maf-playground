using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.CLI.Tui;

/// <summary>Client for the versioned Studio inspection protocol. JSON captures retain the host's native shape.</summary>
internal sealed class StudioClient : IDisposable
{
    private readonly HttpClient _http;

    internal static void AddHttpClient(IServiceCollection services) =>
        services.AddHttpClient(nameof(StudioClient))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });

    public StudioClient(Uri host, IHttpClientFactory factory, string? bearerToken = null)
    {
        if (!host.IsAbsoluteUri || host.UserInfo.Length != 0 || host.Query.Length != 0 || host.Fragment.Length != 0 ||
            !(host.Scheme == Uri.UriSchemeHttps || host.Scheme == Uri.UriSchemeHttp && IsLocalHost(host)))
            throw new ArgumentException("Studio requires an HTTPS URL or a loopback HTTP URL without credentials, query, or fragment.", nameof(host));

        string? token = bearerToken ?? Environment.GetEnvironmentVariable("MAF_STUDIO_TOKEN");
        AuthenticationHeaderValue? authorization = string.IsNullOrWhiteSpace(token) ? null : new("Bearer", token);
        _http = factory.CreateClient(nameof(StudioClient));
        _http.BaseAddress = new Uri(host.AbsoluteUri.TrimEnd('/') + "/api/studio/v1/");
        _http.MaxResponseContentBufferSize = 32 * 1024 * 1024;
        _http.DefaultRequestHeaders.Authorization = authorization;
    }

    private static bool IsLocalHost(Uri host) =>
        string.Equals(host.IdnHost, "localhost", StringComparison.OrdinalIgnoreCase) ||
        IPAddress.TryParse(host.IdnHost.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address);

    public Task<JsonElement> InstanceAsync(CancellationToken ct) => GetAsync("instance", ct);
    public Task<JsonElement> ProfilesAsync(CancellationToken ct) => GetAsync("profiles", ct);
    public Task<JsonElement> EntitiesAsync(string? profileId, CancellationToken ct) =>
        GetAsync(profileId is null ? "entities" : $"entities?profileId={Uri.EscapeDataString(profileId)}", ct);

    public Task<JsonElement> TopologyAsync(string entityId, string profileId, CancellationToken ct) =>
        GetAsync($"topology?entityId={Uri.EscapeDataString(entityId)}&profileId={Uri.EscapeDataString(profileId)}", ct);

    public Task<JsonElement> SessionsAsync(CancellationToken ct) => GetAsync("sessions", ct);
    public Task<JsonElement> CreateSessionAsync(string entityId, string profileId, CancellationToken ct) =>
        PostAsync("sessions", new { entityId, profileId }, ct);

    public Task<JsonElement> ResetSessionAsync(string id, CancellationToken ct) =>
        PostAsync($"sessions/{Segment(id)}/reset", null, ct);

    public Task<JsonElement> StartRunAsync(string requestId, string entityId, string profileId, JsonElement input,
        string? sessionId, CancellationToken ct) =>
        PostAsync("runs", new { requestId, entityId, profileId, input, sessionId }, ct);

    public Task<JsonElement> ActivityAsync(CancellationToken ct) => GetAsync("runs?activity=true", ct);
    public Task<JsonElement> RunsAsync(CancellationToken ct) => GetAsync("runs", ct);
    public Task<JsonElement> RunAsync(string id, CancellationToken ct) => GetAsync($"runs/{Segment(id)}", ct);
    public Task<JsonElement> ResultAsync(string id, CancellationToken ct) => GetAsync($"runs/{Segment(id)}/result", ct);

    public async Task CancelAsync(string id, CancellationToken ct)
    {
        using var response = await _http.PostAsync($"runs/{Segment(id)}/cancel", null, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public Task<JsonElement> PayloadAsync(string id, string payloadId, CancellationToken ct) =>
        GetAsync($"runs/{Segment(id)}/payloads/{Segment(payloadId)}", ct);

    public Task<JsonElement> SpanAsync(string id, string spanId, CancellationToken ct) =>
        GetAsync($"runs/{Segment(id)}/spans/{Segment(spanId)}", ct);

    public Task<JsonElement> EventPageAsync(string id, long after, int limit, CancellationToken ct) =>
        GetAsync(FormattableString.Invariant($"runs/{Segment(id)}/event-page?after={after}&limit={limit}"), ct);

    public Task<JsonElement> CompareAsync(string hostInstanceId, string baselineRunId, string candidateRunId, CancellationToken ct) =>
        PostAsync("comparisons", new { hostInstanceId, baselineRunId, candidateRunId }, ct);

    public async IAsyncEnumerable<JsonElement> WatchAsync(string id, long after, [EnumeratorCancellation] CancellationToken ct)
    {
        string path = FormattableString.Invariant($"runs/{Segment(id)}/events?after={after}");
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new("text/event-stream"));
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessAsync(response, ct);
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Studio returned an unexpected event stream content type.");
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        var events = SseParser.Create(stream, static (_, data) => JsonSerializer.Deserialize<JsonElement>(data));
        await foreach (var item in events.EnumerateAsync(ct))
            yield return item.Data;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        string message = $"Studio returned HTTP {(int)response.StatusCode}.";
        if (response.Content.Headers.ContentType?.MediaType == "application/problem+json")
        {
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            byte[] buffer = new byte[4097];
            int length = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct);
            if (length < buffer.Length)
            {
                try
                {
                    using var problem = JsonDocument.Parse(buffer.AsMemory(0, length));
                    if (problem.RootElement.ValueKind == JsonValueKind.Object &&
                        problem.RootElement.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String &&
                        title.GetString() is "local_access_required" or "authentication_required" or "not_found_or_evicted" or
                            "entity_forbidden" or "capacity_exceeded" or "state_conflict" or "invalid_request" or "operation_failed")
                        message += $" {title.GetString()}";
                }
                catch (JsonException) { } // A proxy may return a malformed problem body.
            }
        }
        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static string Segment(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id is "." or ".." || id.Contains('/') || id.Contains('\\'))
            throw new ArgumentException("Invalid Studio resource identifier.", nameof(id));
        return Uri.EscapeDataString(id);
    }

    private async Task<JsonElement> GetAsync(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    private async Task<JsonElement> PostAsync(string path, object? body, CancellationToken ct)
    {
        using var content = body is null ? null : JsonContent.Create(body);
        using var response = await _http.PostAsync(path, content, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    public void Dispose() => _http.Dispose();
}
