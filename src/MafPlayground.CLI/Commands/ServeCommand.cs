using System.CommandLine;
using MafPlayground.AI;
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

public sealed class ServeCommand : ICliCommand
{
    public int Order => 410;
    Command ICliCommand.Create() => Create();

    public static Command Create(Func<ServeCommandOptions, CancellationToken, Task<int>>? runAsync = null)
    {
        runAsync ??= RunAsync;
        Option<string?> model = new("--model", "-m") { Description = "Model selector in provider:model format. Falls back to AI_MODEL." };
        Option<string> url = new("--url") { Description = "Loopback HTTP URL for Studio.", DefaultValueFactory = _ => "http://localhost:5051" };
        Command command = new("serve", "Start the Studio HTTP API for tui --host.");
        command.Options.Add(model);
        command.Options.Add(url);
        command.SetAction((result, token) => runAsync(new(result.GetValue(model), result.GetValue(url)!), token));
        return command;
    }

    private static async Task<int> RunAsync(ServeCommandOptions options, CancellationToken token)
    {
        using ILoggerFactory logging = CommandLogging.CreateLoggerFactory();
        ILogger logger = logging.CreateLogger<ServeCommand>();
        try
        {
            var access = new StudioAccessPolicy(options.Url, ["basic", "translation", "basic-rag", "help"]);
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
            builder.Services.AddSingleton(await StudioSourceRevision.ReadAsync(Environment.CurrentDirectory, token));
            builder.Services.AddLocalUserContext();
            builder.Services.AddAIRuntime(builder.Configuration, AIStartupValidationMode.LocalInspection).AddMafPlaygroundSampleEntities();
            builder.Services.AddInMemoryAgentSessions();
            builder.Services.AddSingleton(RepositoryHelpAgentConfiguration.Definition);
            builder.Services.ConfigureLocalEntityOptions(builder.Configuration);
            builder.Services.AddConfiguredAIProviders(builder.Configuration);
            AddOptionalRetrieval(builder.Services, builder.Configuration);
            builder.Services.AddMafPlaygroundObservability(builder.Configuration);

            builder.Services.AddAuthorization(options =>
            {
                options.AddPolicy("AI.Basic", p => p.RequireClaim("ai_permission", "basic"));
                options.AddPolicy("AI.Translation", p => p.RequireClaim("ai_permission", "translation"));
                options.AddPolicy("AI.BasicRag", p => p.RequireClaim("ai_permission", "basic-rag"));
                options.AddPolicy("AI.Help", p => p.RequireClaim("ai_permission", "help"));
            });
            builder.Services.AddStudio(builder.Configuration);
            await using WebApplication app = builder.Build();
            app.UseStudioAccess();
            app.MapStudio();
            await app.StartAsync(token);
            Console.WriteLine($"Studio API: {access.Endpoint.GetLeftPart(UriPartial.Authority)}");
            Console.WriteLine($"Local API bearer token: {access.BearerToken}");
            await app.WaitForShutdownAsync(token);
            return 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return 130; }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            // Configuration and provider exceptions can contain credentials; expose only a stable category.
            logger.LogError("Studio could not start. Verify local URL, model, and configuration.");
            return 2;
        }
    }

    internal static void AddOptionalRetrieval(IServiceCollection services, IConfiguration configuration)
    {
        if (configuration.GetValue<bool>("Studio:EnableRetrieval")) services.AddConfiguredRetrieval(configuration);
    }
}

public sealed record ServeCommandOptions(string? Model, string Url);
