using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using MafPlayground.AI;
using MafPlayground.AI.Agents.BasicAgent;
using MafPlayground.AI.Agents.BasicRagAgent;
using MafPlayground.AI.Agents.HelpAgent;
using MafPlayground.AI.Guards;
using MafPlayground.AI.Resilience;
using MafPlayground.AI.Workflows.Translation;
using MafPlayground.CLI.Documentation;
using MafPlayground.CLI.Extensions;
using MafPlayground.CLI.Helpers;
using MafPlayground.Observability;
using MafPlayground.Studio.Http;
using MafPlayground.Studio.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MafPlayground.CLI.Commands;

public sealed class StudioCommand : ICliCommand
{
    public int Order => 410;
    Command ICliCommand.Create() => Create();

    public static Command Create(Func<StudioCommandOptions, CancellationToken, Task<int>>? runAsync = null)
    {
        runAsync ??= RunAsync;
        Option<string?> model = new("--model", "-m") { Description = "Model selector in provider:model format. Falls back to AI_MODEL." };
        Option<string> url = new("--url") { Description = "Loopback HTTP URL for Studio.", DefaultValueFactory = _ => "http://localhost:5051" };
        Option<bool> noOpen = new("--no-open") { Description = "Do not open the browser; print local access instructions." };
        Option<string?> export = new("--export-contracts") { Description = "Export transport and entity JSON schemas offline and exit." };
        Command command = new("studio", "Run Local Agent Studio in this CLI process.");
        command.Options.Add(model);
        command.Options.Add(url);
        command.Options.Add(noOpen);
        command.Options.Add(export);
        command.SetAction((result, token) => runAsync(new(result.GetValue(model), result.GetValue(url)!, result.GetValue(noOpen), result.GetValue(export)), token));
        return command;
    }

    private static async Task<int> RunAsync(StudioCommandOptions options, CancellationToken token)
    {
        using ILoggerFactory logging = CommandLogging.CreateLoggerFactory();
        ILogger logger = logging.CreateLogger<StudioCommand>();
        try
        {
            if (options.ExportContracts is { } export)
            {
                // Registration metadata only: no host start, provider registration, or model construction.
                ServiceCollection services = new();
                var registrations = services.AddAIRuntime(new ConfigurationBuilder().Build()).AddMafPlaygroundSampleEntities().Registrations;
                JsonElement schema = StudioContractSchema.Create(registrations);
                string path = Path.GetFullPath(export);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true }), token);
                return 0;
            }

            var access = new StudioAccessPolicy(options.Url);
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            { ContentRootPath = AppContext.BaseDirectory, EnvironmentName = Environments.Development });
            string? selector = options.Model ?? builder.Configuration["AI_MODEL"];
            if (!string.IsNullOrEmpty(selector))
            {
                _ = StudioProfileCatalog.ParseModel(selector);
                builder.Configuration["AI:Defaults:Model"] = selector;
            }
            builder.WebHost.UseUrls(access.Endpoint.GetLeftPart(UriPartial.Authority));
            builder.Services.AddOptions<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>()
                .Configure<StudioOptions>((server, limits) => server.Limits.MaxRequestBodySize = (long)limits.MaxPayloadBytes + 4096);
            builder.Services.AddSingleton(access);
            builder.Services.AddLocalUserContext();
            builder.Services.AddAIRuntime(builder.Configuration, AIStartupValidationMode.LocalInspection).AddMafPlaygroundSampleEntities();
            builder.Services.AddInMemoryAgentSessions();
            builder.Services.AddSingleton(RepositoryHelpAgentConfiguration.Definition);
            builder.Services.AddSingleton<IHelpCapabilityCatalog>(new SystemCommandLineHelpCapabilityCatalog(Parser.CreateRootCommand()));
            builder.Services.Configure<BasicAgentOptions>(builder.Configuration.GetSection(BasicAgentOptions.ConfigurationSectionName));
            builder.Services.Configure<BasicRagAgentOptions>(builder.Configuration.GetSection(BasicRagAgentOptions.ConfigurationSectionName));
            builder.Services.Configure<HelpAgentOptions>(builder.Configuration.GetSection(RepositoryHelpAgentConfiguration.ConfigurationSectionName));
            builder.Services.Configure<TranslationWorkflowOptions>(builder.Configuration.GetSection("AI:Workflows:Translation"));
            builder.Services.Configure<AIResilienceOptions>(builder.Configuration.GetSection(AIResilienceOptions.ConfigurationSectionName));
            builder.Services.Configure<AIGuardOptions>(builder.Configuration.GetSection(AIGuardOptions.ConfigurationSectionName));
            builder.Services.AddConfiguredAIProviders(builder.Configuration);
            AddOptionalRetrieval(builder.Services, builder.Configuration);
            builder.Services.AddMafPlaygroundObservability(builder.Configuration);
            builder.Services.AddStudio(builder.Configuration);
            await using WebApplication app = builder.Build();
            app.UseStudioAccess();
            bool hasFrontend = app.UseStudioFrontend(Path.Combine(AppContext.BaseDirectory, "studio"));
            app.MapStudio();
            await app.StartAsync(token);
            string login = access.Endpoint.GetLeftPart(UriPartial.Authority) + "/studio/login";
            // Credentials appear only on this local console, never in URLs or structured telemetry.
            Console.WriteLine($"Studio: {login}\nOne-time browser access code: {access.BootstrapCode}");
            Console.WriteLine($"Local API bearer token: {access.BearerToken}");
            if (!hasFrontend) Console.WriteLine("Frontend assets are not installed. Run ./scripts/build-studio.sh, rebuild or publish the CLI, then restart. HTTP API and MCP remain usable with --no-open.");
            if (!options.NoOpen && hasFrontend)
            {
                try { Process.Start(new ProcessStartInfo(login) { UseShellExecute = true }); }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                { logger.LogInformation("Open the printed Studio URL in your browser."); }
            }
            await app.WaitForShutdownAsync(token);
            return 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return 130; }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            // Configuration and provider exceptions can contain credentials; expose only a stable category.
            logger.LogError("Studio could not start or export contracts. Verify local URL, model, and configuration.");
            return 2;
        }
    }

    internal static void AddOptionalRetrieval(IServiceCollection services, IConfiguration configuration)
    {
        if (configuration.GetValue<bool>("Studio:EnableRetrieval")) services.AddConfiguredRetrieval(configuration);
    }
}

public sealed record StudioCommandOptions(string? Model, string Url, bool NoOpen, string? ExportContracts);
