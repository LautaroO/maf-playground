using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace MafPlayground.Studio.IntegrationTests;

public sealed class StudioPackagedTests
{
    [StudioPackagingFact]
    [Trait("Category", "StudioPackaging")]
    public async Task Published_cli_serves_bearer_authenticated_api_without_frontend_assets()
    {
        string publish = Environment.GetEnvironmentVariable("STUDIO_PUBLISH_DIRECTORY")!;
        Assert.False(Directory.Exists(Path.Combine(publish, "studio")));
        Assert.True(File.Exists(Path.Combine(publish, "MafPlayground.CLI.dll")));
        string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ??
            Environment.GetEnvironmentVariable("PATH")!.Split(Path.PathSeparator).Select(path => Path.Combine(path, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet")).First(File.Exists);
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var start = new ProcessStartInfo(dotnet) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = publish };
        foreach (string arg in new[] { Path.Combine(publish, "MafPlayground.CLI.dll"), "serve", "--url", $"http://127.0.0.1:{port}" }) start.ArgumentList.Add(arg);
        foreach (string name in start.Environment.Keys.Where(name => name.StartsWith("AI", StringComparison.OrdinalIgnoreCase) || name.StartsWith("STUDIO", StringComparison.OrdinalIgnoreCase) || name.StartsWith("OBSERVABILITY", StringComparison.OrdinalIgnoreCase) || name.StartsWith("OTEL_", StringComparison.OrdinalIgnoreCase) || name is "GEMINI_API_KEY" or "GOOGLE_API_KEY").ToArray()) start.Environment.Remove(name);
        start.Environment["PATH"] = ""; // Only the resolved .NET executable can run; Node is unavailable.
        start.Environment["AI_MODEL"] = "";
        start.Environment["AI__Defaults__Model"] = "";
        start.Environment["Studio__EnableRetrieval"] = "false";
        start.Environment["Observability__Enabled"] = "false";
        using var process = Process.Start(start)!;
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        Task<string>? stdout = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        try
        {
            string? bearer = null;
            while (bearer is null)
            {
                string? line = await process.StandardOutput.ReadLineAsync(timeout.Token);
                Assert.NotNull(line);
                const string prefix = "Local API bearer token: ";
                if (line.StartsWith(prefix, StringComparison.Ordinal)) bearer = line[prefix.Length..];
            }
            stdout = process.StandardOutput.ReadToEndAsync();
            using var client = new HttpClient { BaseAddress = new($"http://127.0.0.1:{port}") };
            const string entities = "/api/studio/v1/entities";
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(entities, timeout.Token)).StatusCode);
            client.DefaultRequestHeaders.Authorization = new("Bearer", bearer);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(entities, timeout.Token)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/studio", timeout.Token)).StatusCode);
            client.DefaultRequestHeaders.Add("Origin", "http://evil.test");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(entities, timeout.Token)).StatusCode);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await stderr;
            if (stdout is not null) await stdout;
        }
    }

    private sealed class StudioPackagingFactAttribute : FactAttribute
    {
        public StudioPackagingFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STUDIO_PUBLISH_DIRECTORY")))
                Skip = "Set STUDIO_PUBLISH_DIRECTORY to the built CLI publish directory.";
        }
    }
}
