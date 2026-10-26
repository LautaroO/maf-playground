using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using MafPlayground.CLI.Commands;
using MafPlayground.Studio.Contracts;
using Xunit;

namespace MafPlayground.Studio.IntegrationTests;

public sealed class StudioStartupTests
{
    [Fact]
    public async Task Existing_cli_starts_one_loopback_host_without_model_or_retrieval_and_shuts_down()
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        string isolated = Path.Combine(Path.GetTempPath(), "studio-startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolated);
        foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(typeof(StudioCommand).Assembly.Location)!))
            File.Copy(file, Path.Combine(isolated, Path.GetFileName(file)));
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in new[] { Path.Combine(isolated, "MafPlayground.CLI.dll"), "studio", "--no-open", "--url", $"http://127.0.0.1:{port}" }) start.ArgumentList.Add(argument);
        foreach (string name in start.Environment.Keys.Where(name => name.StartsWith("AI_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("STUDIO", StringComparison.OrdinalIgnoreCase) || name.StartsWith("OBSERVABILITY", StringComparison.OrdinalIgnoreCase) || name.StartsWith("OTEL_", StringComparison.OrdinalIgnoreCase) || name is "GEMINI_API_KEY" or "GOOGLE_API_KEY").ToArray()) start.Environment.Remove(name);
        start.Environment["AI_MODEL"] = "";
        start.Environment["AI__Defaults__Model"] = "";
        start.Environment["Studio__EnableRetrieval"] = "false";
        start.Environment["Studio__Limits__MaxPayloadBytes"] = (3 * 1024 * 1024).ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["Observability__Enabled"] = "false";
        using var process = Process.Start(start)!;
        Task<string> errors = process.StandardError.ReadToEndAsync();
        Task<string>? drain = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            string? code = null;
            string? bearer = null;
            while (code is null || bearer is null)
            {
                string? line = await process.StandardOutput.ReadLineAsync(timeout.Token);
                Assert.NotNull(line);
                const string prefix = "One-time browser access code: ";
                if (line.StartsWith(prefix, StringComparison.Ordinal)) code = line[prefix.Length..];
                const string bearerPrefix = "Local API bearer token: ";
                if (line.StartsWith(bearerPrefix, StringComparison.Ordinal)) bearer = line[bearerPrefix.Length..];
            }
            drain = process.StandardOutput.ReadToEndAsync();
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true }) { BaseAddress = new($"http://127.0.0.1:{port}") };
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/studio/v1/entities", timeout.Token)).StatusCode);
            const string malformed = "/api/studio/v1/topology?entityId=basic-agent";
            await AssertSafeBindingProblem(await client.GetAsync(malformed, timeout.Token), HttpStatusCode.Unauthorized, "authentication_required", bearer, null);
            client.DefaultRequestHeaders.Authorization = new("Bearer", bearer);
            await AssertSafeBindingProblem(await client.GetAsync(malformed, timeout.Token), HttpStatusCode.BadRequest, "invalid_request", bearer, null);
            client.DefaultRequestHeaders.Authorization = null;
            var login = await client.PostAsync("/studio/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = code }), timeout.Token);
            Assert.Equal(HttpStatusCode.SeeOther, login.StatusCode);
            string cookie = Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';')[0];
            await AssertSafeBindingProblem(await client.GetAsync(malformed, timeout.Token), HttpStatusCode.BadRequest, "invalid_request", bearer, cookie[(cookie.IndexOf('=') + 1)..]);
            var entities = (await client.GetFromJsonAsync<StudioEntityDescriptor[]>("/api/studio/v1/entities", timeout.Token))!;
            Assert.Equal(4, entities.Length);
            Assert.All(entities, entity => Assert.False(entity.IsAvailable));
            // A configured valid limit above 2 MiB must reach preflight, not Kestrel's old default cap.
            using var large = await client.PostAsJsonAsync("/api/studio/v1/runs", new StudioRunRequest(
                "configured-limit", "basic-agent", "cli-default", JsonSerializer.SerializeToElement(new { message = new string('x', 2 * 1024 * 1024 + 8192) }), null), timeout.Token);
            Assert.Equal(HttpStatusCode.Conflict, large.StatusCode);
            string missingAssets = await client.GetStringAsync("/studio", timeout.Token);
            Assert.Contains("Frontend assets are not installed", missingAssets);
            Assert.Contains("scripts/build-studio.sh", missingAssets);
            Assert.Contains("--no-open", missingAssets);
            using var headers = await client.GetAsync("/studio", timeout.Token);
            string csp = Assert.Single(headers.Headers.GetValues("Content-Security-Policy"));
            Assert.Contains("base-uri 'self'", csp);
            Assert.Contains("worker-src 'self'", csp);
            Assert.DoesNotContain("unsafe-eval", csp);
            Assert.DoesNotContain("ws:", csp);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/v1/entities", timeout.Token)).StatusCode);
            if (!OperatingSystem.IsWindows())
            {
                using var signal = Process.Start(new ProcessStartInfo("kill") { ArgumentList = { "-TERM", process.Id.ToString() }, UseShellExecute = false })!;
                await signal.WaitForExitAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                Assert.Equal(0, process.ExitCode);
                await drain;
            }
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await errors;
            if (drain is not null) await drain;
            Directory.Delete(isolated, recursive: true);
        }
    }
    [Theory]
    [InlineData("--url", "http://0.0.0.0:5051")]
    [InlineData("--model", "provider:model?token=synthetic-config-canary")]
    public async Task Invalid_startup_configuration_returns_actionable_sanitized_error(string option, string value)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in new[] { typeof(StudioCommand).Assembly.Location, "studio", "--no-open", option, value }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(2, process.ExitCode);
            string output = await stdout + await stderr;
            Assert.Contains("Verify local URL, model, and configuration", output);
            Assert.DoesNotContain("synthetic-config-canary", output);
            Assert.DoesNotContain("Local API bearer token", output);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    private static async Task AssertSafeBindingProblem(HttpResponseMessage response, HttpStatusCode status, string title, string bearer, string? cookie)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            string body = await response.Content.ReadAsStringAsync();
            Assert.False(body.Contains(bearer, StringComparison.Ordinal) || cookie is not null && body.Contains(cookie, StringComparison.Ordinal), "Development response leaked an access credential.");
            Assert.False(body.Contains("Exception", StringComparison.Ordinal) || body.Contains("Microsoft.AspNetCore", StringComparison.Ordinal) || body.Contains("HEADERS", StringComparison.Ordinal));
            using var problem = JsonDocument.Parse(body);
            Assert.Equal(title, problem.RootElement.GetProperty("title").GetString());
            Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("correlationId").GetString()));
            Assert.False(problem.RootElement.TryGetProperty("detail", out _));
        }
    }

}
