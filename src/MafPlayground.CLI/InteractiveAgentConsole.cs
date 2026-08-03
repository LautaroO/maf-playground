using System.Diagnostics;
using MafPlayground.AI;
using MafPlayground.Observability;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MafPlayground.CLI;

public sealed class InteractiveAgentConsole
{
    private static readonly ActivitySource ActivitySource =
        new(ObservabilityTelemetry.TestHarnessSourceName);
    private static readonly TimeSpan TurnTimeout = TimeSpan.FromMinutes(5);

    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly ILogger<InteractiveAgentConsole> _logger;

    public InteractiveAgentConsole()
        : this(Console.In, Console.Out, Console.Error, NullLogger<InteractiveAgentConsole>.Instance)
    {
    }

    public InteractiveAgentConsole(ILogger<InteractiveAgentConsole> logger)
        : this(Console.In, Console.Out, Console.Error, logger)
    {
    }

    public InteractiveAgentConsole(TextReader input, TextWriter output, TextWriter error)
        : this(input, output, error, NullLogger<InteractiveAgentConsole>.Instance)
    {
    }

    private InteractiveAgentConsole(
        TextReader input,
        TextWriter output,
        TextWriter error,
        ILogger<InteractiveAgentConsole> logger)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _error = error ?? throw new ArgumentNullException(nameof(error));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<int> RunAsync(
        AIAgent agent,
        AIModelSelection modelSelection,
        string? prompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(modelSelection);

        AgentSession session = await agent.CreateSessionAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(prompt))
        {
            return await RunTurnAsync(
                agent,
                session,
                modelSelection,
                prompt,
                "single",
                cancellationToken) ? 0 : 1;
        }

        await _output.WriteLineAsync("Basic agent ready. Type /exit to quit.");

        while (!cancellationToken.IsCancellationRequested)
        {
            await _output.WriteAsync("> ");
            await _output.FlushAsync(cancellationToken);

            string? input = await _input.ReadLineAsync(cancellationToken);
            if (input is null || IsExitCommand(input))
            {
                return 0;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            await RunTurnAsync(
                agent,
                session,
                modelSelection,
                input,
                "interactive",
                cancellationToken);
        }

        return 0;
    }

    private async Task<bool> RunTurnAsync(
        AIAgent agent,
        AgentSession session,
        AIModelSelection modelSelection,
        string prompt,
        string mode,
        CancellationToken cancellationToken)
    {
        using Activity? activity = ActivitySource.StartActivity(
            "agent.test.turn",
            ActivityKind.Internal);
        activity?.SetTag("agent.name", agent.Name);
        activity?.SetTag("gen_ai.provider.name", modelSelection.Provider);
        activity?.SetTag("gen_ai.request.model", modelSelection.Model);
        activity?.SetTag("maf_playground.harness.mode", mode);
        _logger.LogInformation(
            "Starting agent test turn with {Provider} model {Model} in {Mode} mode",
            modelSelection.Provider,
            modelSelection.Model,
            mode);

        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TurnTimeout);

        try
        {
            await _output.WriteAsync("Agent: ");

            await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(
                               prompt,
                               session,
                               cancellationToken: timeoutSource.Token))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    await _output.WriteAsync(update.Text);
                    await _output.FlushAsync(timeoutSource.Token);
                }
            }

            await _output.WriteLineAsync();
            activity?.SetTag("maf_playground.outcome", "success");
            _logger.LogInformation("Agent test turn completed successfully");
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            activity?.SetTag("maf_playground.outcome", "timeout");
            activity?.SetStatus(ActivityStatusCode.Error, "Agent request timed out.");
            _logger.LogWarning("Agent test turn timed out after {Timeout}", TurnTimeout);
            await _error.WriteLineAsync($"The agent request timed out after {TurnTimeout.TotalMinutes:0} minutes.");
            return false;
        }
        catch (OperationCanceledException)
        {
            activity?.SetTag("maf_playground.outcome", "cancelled");
            _logger.LogInformation("Agent test turn was cancelled");
            throw;
        }
        catch (Exception exception)
        {
            activity?.SetTag("maf_playground.outcome", "error");
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            _logger.LogError(
                "Agent test turn failed with {ExceptionType}",
                exception.GetType().FullName);
            await _error.WriteLineAsync($"Agent request failed: {exception.Message}");
            return false;
        }
    }

    private static bool IsExitCommand(string input)
    {
        string command = input.Trim();
        return command.Equals("/exit", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("quit", StringComparison.OrdinalIgnoreCase);
    }
}
