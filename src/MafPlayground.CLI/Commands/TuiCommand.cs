using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using MafPlayground.AI;
using MafPlayground.CLI.Extensions;
using MafPlayground.CLI.Inspection;
using MafPlayground.CLI.Tui;
using MafPlayground.Observability;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MafPlayground.CLI.Commands;

internal sealed class TuiCommand : ICliCommand
{
    public int Order => 510;
    public Command Create()
    {
        Option<string?> model = new("--model", "-m") { Description = "Model selector; defaults to AI_MODEL." };
        Option<string?> input = new("--input") { Description = "Load the run input from a JSON file." };
        Option<string?> host = new("--host") { Description = "Connect to a Studio API host (MAF_STUDIO_TOKEN)." };
        Option<string?> entity = new("--entity") { Description = "Open a local entity by identifier." };
        Command command = new("tui", "Inspect and run local agents and workflows in the terminal.");
        command.Options.Add(model);
        command.Options.Add(input);
        command.Options.Add(entity);
        host.Validators.Add(result =>
        {
            if (result.Tokens.Count != 1 || !Uri.TryCreate(result.Tokens[0].Value, UriKind.Absolute, out _))
                result.AddError("--host requires an absolute Studio URL.");
        });
        command.Options.Add(host);
        command.SetAction((result, token) => RunAsync(result.GetValue(model), result.GetValue(input), result.GetValue(entity), result.GetValue(host), token));
        return command;
    }

    private static async Task<int> RunAsync(string? model, string? file, string? entity, string? host, CancellationToken token)
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine("tui requires an interactive terminal.");
            return 2;
        }
        if (host is not null)
        {
            if (model is not null)
            {
                Console.Error.WriteLine("Select a server profile in remote mode. --model applies to local execution.");
                return 2;
            }
            return await RemoteStudio.RunAsync(new Uri(host, UriKind.Absolute), entity, file, token);
        }
        var initial = LocalEntityCatalog.All.FirstOrDefault(d => d.Id == entity);
        if (entity is not null && initial is null)
        {
            Console.Error.WriteLine("Unknown entity. Use inspect list to see available entities.");
            return 2;
        }
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            string? input = file is null ? null : await File.ReadAllTextAsync(file, token);
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
            builder.Logging.ClearProviders();
            string? defaultModel = model ?? builder.Configuration["AI_MODEL"];
            var terminal = new StudioTerminal(stop);
            var workspace = new LocalWorkspace((descriptor, firstInput) => new InspectorScreen(descriptor, firstInput,
                builder.Configuration, defaultModel, ExecuteAsync, stop.Token), input);
            var paletteKey = ActionPicker.AtRoot(workspace.AvailableActions);
            StudioTerminal.App.Keyboard.KeyDown += paletteKey;
            bool interrupted;
            try
            {
                workspace.Start(initial);
                StudioTerminal.RunRoot();
                interrupted = stop.IsCancellationRequested;
            }
            finally
            {
                StudioTerminal.App.Keyboard.KeyDown -= paletteKey;
                terminal.Dispose();
                await terminal.CancelAsync().ConfigureAwait(false);
                await workspace.FinishAsync().ConfigureAwait(false);
            }
            return interrupted ? 130 : 0;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 130; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine(InspectorRun.Clean(e.Message));
            return 2;
        }
    }

    internal static async Task ExecuteAsync(LocalEntityDescriptor entity, string json, string? model,
        Action<Action<InspectorRun>> publish, CancellationToken token)
    {
        object input = JsonSerializer.Deserialize(json, entity.InputType, InspectorRun.Json)
            ?? throw new JsonException("Input must have a value.");
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        if (!AIModelSelection.TryParse(model ?? builder.Configuration["AI_MODEL"], out var selection))
            throw new ArgumentException("Set AI_MODEL or pass --model provider:model to execute.");
        builder.Services.AddLocalRuntime(builder.Configuration, selection, entity.RequiresRetrieval);
        if (entity.RequiresRetrieval) builder.Services.AddConfiguredRetrieval(builder.Configuration);
        builder.Services.AddMafPlaygroundObservability(builder.Configuration);
        using var host = builder.Build();
        await host.StartAsync(token);
        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            if (entity.StreamWorkflow is not null)
            {
                bool terminalEventSeen = false;
                await foreach (var item in entity.StreamWorkflow(scope.ServiceProvider, input, token))
                {
                    long timestamp = Stopwatch.GetTimestamp();
                    publish(run => run.Apply(item, timestamp));
                    terminalEventSeen |= item is WorkflowOutputEvent or WorkflowErrorEvent;
                }
                if (!terminalEventSeen)
                {
                    token.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("Workflow ended without a result.");
                }
                return;
            }

            string prompt = input switch
            {
                AI.Agents.BasicAgent.BasicAgentRequest request => request.Message,
                string value => value,
                _ => throw new ArgumentException("Unsupported agent input."),
            };
            ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
            long startedAt = Stopwatch.GetTimestamp();
            publish(run => run.Begin(entity.Id, input, startedAt));
            var agent = InspectorToolCapture.Attach(entity.CreateAgent!(scope.ServiceProvider), publish);
            try
            {
                var response = await agent.RunAsync(prompt, cancellationToken: token);
                long finishedAt = Stopwatch.GetTimestamp();
                publish(run =>
                {
                    run.End(entity.Id, response.Text, timestamp: finishedAt);
                    run.Complete(response.Text, response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount);
                });
            }
            catch (Exception e) when (!token.IsCancellationRequested)
            {
                long finishedAt = Stopwatch.GetTimestamp();
                publish(run => run.End(entity.Id, null, e.Message, finishedAt));
                throw;
            }
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }
}
