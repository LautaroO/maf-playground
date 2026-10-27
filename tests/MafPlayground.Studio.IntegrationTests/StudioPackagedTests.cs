using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace MafPlayground.Studio.IntegrationTests;

public sealed class StudioPackagedTests
{
    [StudioPackagingFact]
    [Trait("Category", "StudioPackaging")]
    public async Task Published_cli_serves_authenticated_routes_workers_and_fonts_without_node()
    {
        string publish = Environment.GetEnvironmentVariable("STUDIO_PUBLISH_DIRECTORY")!;
        string assets = Path.Combine(publish, "studio");
        Assert.True(File.Exists(Path.Combine(assets, "index.html")), "Build assets and publish the CLI first.");
        Assert.False(Directory.EnumerateFiles(publish, "*StudioFixture*", SearchOption.AllDirectories).Any());
        foreach (string name in new[] { "DM-Sans-OFL.txt", "IBM-Plex-Mono-OFL.txt", "Monaco-LICENSE", "Monaco-ThirdPartyNotices.txt", "Codicons-CC-BY-4.0.txt", "Codicons-MIT.txt", "Codicons-NOTICE.txt", "ThirdParty-LICENSES.txt" })
            Assert.True(File.Exists(Path.Combine(assets, "licenses", name)), "Missing bundled font/editor license.");
        string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ??
            Environment.GetEnvironmentVariable("PATH")!.Split(Path.PathSeparator).Select(path => Path.Combine(path, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet")).First(File.Exists);
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var start = new ProcessStartInfo(dotnet) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = publish };
        foreach (string arg in new[] { Path.Combine(publish, "MafPlayground.CLI.dll"), "studio", "--no-open", "--url", $"http://127.0.0.1:{port}" }) start.ArgumentList.Add(arg);
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
            string[] scripts = Directory.GetFiles(assets, "*.js");
            Assert.Contains(scripts, path => Path.GetFileName(path).StartsWith("worker-", StringComparison.Ordinal));
            foreach (string route in new[] { "/studio", "/studio/catalog", "/studio/runs/opaque", "/studio/compare", "/studio/" + Path.GetFileName(scripts[0]) })
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route, timeout.Token)).StatusCode);
            client.DefaultRequestHeaders.Authorization = new("Bearer", bearer);
            string index = await File.ReadAllTextAsync(Path.Combine(assets, "index.html"), timeout.Token);
            Assert.Contains("<base href=\"/studio/\"", index);
            foreach (string route in new[] { "/studio", "/studio/", "/studio/catalog", "/studio/runs/opaque", "/studio/compare" })
            {
                using var response = await client.GetAsync(route, timeout.Token);
                Assert.Equal(index, await response.Content.ReadAsStringAsync(timeout.Token));
                string csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
                Assert.Contains("worker-src 'self'", csp);
                Assert.Contains("style-src 'self' 'unsafe-inline'", csp);
                Assert.Contains("base-uri 'self'", csp);
                Assert.DoesNotContain("unsafe-eval", csp);
                Assert.DoesNotContain("ws:", csp);
            }
            foreach (string file in scripts.Concat(Directory.GetFiles(assets, "*.woff2", SearchOption.AllDirectories)).Concat(Directory.GetFiles(assets, "*.ttf", SearchOption.AllDirectories)))
            {
                using var response = await client.GetAsync("/studio/" + Path.GetRelativePath(assets, file).Replace('\\', '/'), timeout.Token);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains(response.Content.Headers.ContentType!.MediaType, new[] { "text/javascript", "application/javascript", "font/woff2", "font/ttf" });
                Assert.Equal(new FileInfo(file).Length, (await response.Content.ReadAsByteArrayAsync(timeout.Token)).LongLength);
            }
            foreach (string route in new[] { "/studio/missing.js", "/studio/missing.woff2", "/studio/media/missing.ttf", "/v1/entities" })
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(route, timeout.Token)).StatusCode);
            client.DefaultRequestHeaders.Add("Origin", "http://evil.test");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/studio/catalog", timeout.Token)).StatusCode);
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
                Skip = "Set STUDIO_PUBLISH_DIRECTORY to the built CLI publish directory (scripts/verify-studio-package.sh).";
        }
    }
}
