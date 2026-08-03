using MafPlayground.AI;
using MafPlayground.AI.Agents.BasicAgent;
using MafPlayground.CLI;
using MafPlayground.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

return await Parser.CreateRootCommand(RunBasicAgentAsync).Parse(args).InvokeAsync();

static async Task<int> RunBasicAgentAsync(
    BasicAgentCommandOptions commandOptions,
    CancellationToken cancellationToken)
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    builder.Logging.ClearProviders();

    ConfigurationManager configuration = builder.Configuration;

    string? modelSelector = commandOptions.Model
        ?? configuration["AI_MODEL"];

    if (!AIModelSelection.TryParse(modelSelector, out AIModelSelection? modelSelection))
    {
        Console.Error.WriteLine(
            "An AI model is required in 'provider:model' format. Use --model or set AI_MODEL.");
        return 2;
    }

    builder.Services.AddAIServices(modelSelection);
    builder.Services.AddConfiguredAIProviders(configuration);
    builder.Services.AddMafPlaygroundObservability(configuration);
    builder.Services.AddSingleton<InteractiveAgentConsole>();

    try
    {
        using IHost host = builder.Build();
        bool hostStarted = false;

        try
        {
            await host.StartAsync(cancellationToken);
            hostStarted = true;

            BasicAgent basicAgent = host.Services.GetRequiredService<BasicAgent>();
            InteractiveAgentConsole console =
                host.Services.GetRequiredService<InteractiveAgentConsole>();
            return await console.RunAsync(
                basicAgent.Agent,
                modelSelection,
                commandOptions.Prompt,
                cancellationToken);
        }
        finally
        {
            if (hostStarted)
            {
                await host.StopAsync(CancellationToken.None);
            }
        }
    }
    catch (AIProviderNotFoundException exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 2;
    }
    catch (OptionsValidationException exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 2;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        return 130;
    }
}
