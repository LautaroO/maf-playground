using System.Diagnostics;
using MafPlayground.AI;
using MafPlayground.Observability;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MafPlayground.CLI;

public sealed class InteractiveAgentConsole
{
    private static readonly ActivitySource ActivitySource =
        new(ObservabilityTelemetry.TestHarnessSourceName);
    private static readonly TimeSpan DefaultTurnTimeout = TimeSpan.FromMinutes(5);

    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly ILogger<InteractiveAgentConsole> _logger;
    private readonly TimeSpan _turnTimeout;

    public InteractiveAgentConsole()
        : this(
            Console.In,
            Console.Out,
            Console.Error,
            NullLogger<InteractiveAgentConsole>.Instance,
            DefaultTurnTimeout)
    {
    }

    public InteractiveAgentConsole(ILogger<InteractiveAgentConsole> logger)
        : this(Console.In, Console.Out, Console.Error, logger, DefaultTurnTimeout)
    {
    }

    public InteractiveAgentConsole(TextReader input, TextWriter output, TextWriter error)
        : this(
            input,
            output,
            error,
            NullLogger<InteractiveAgentConsole>.Instance,
            DefaultTurnTimeout)
    {
    }

    public InteractiveAgentConsole(
        TextReader input,
        TextWriter output,
        TextWriter error,
        TimeSpan turnTimeout)
        : this(
            input,
            output,
            error,
            NullLogger<InteractiveAgentConsole>.Instance,
            turnTimeout)
    {
    }

    private InteractiveAgentConsole(
        TextReader input,
        TextWriter output,
        TextWriter error,
        ILogger<InteractiveAgentConsole> logger,
        TimeSpan turnTimeout)
    {
        if (turnTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(turnTimeout),
                "The turn timeout must be greater than zero.");
        }

        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _error = error ?? throw new ArgumentNullException(nameof(error));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _turnTimeout = turnTimeout;
    }

    public async Task<int> RunAsync(
        AIAgent agent,
        AIModelSelection modelSelection,
        string? prompt,
        bool watch = false,
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
                watch,
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
                watch,
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
        bool watch,
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

        Stopwatch elapsed = Stopwatch.StartNew();
        string outcome = "success";
        string? errorType = null;
        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_turnTimeout);

        try
        {
            Dictionary<string, (string Name, TimeSpan StartedAt)> toolCalls = [];
            if (watch)
            {
                await WriteWatchEventAsync(
                    elapsed,
                    $"agent {agent.Name ?? "unnamed"} started");
            }

            await _output.WriteAsync("Agent: ");

            await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(
                               prompt,
                               session,
                               cancellationToken: timeoutSource.Token))
            {
                if (watch)
                {
                    foreach (AIContent content in update.Contents)
                    {
                        if (content is FunctionCallContent functionCall &&
                            toolCalls.TryAdd(
                                functionCall.CallId,
                                (functionCall.Name, elapsed.Elapsed)))
                        {
                            await WriteWatchEventAsync(
                                elapsed,
                                $"tool {functionCall.Name} called");
                        }
                        else if (content is FunctionResultContent functionResult &&
                                 toolCalls.Remove(
                                     functionResult.CallId,
                                     out (string Name, TimeSpan StartedAt) toolCall))
                        {
                            await WriteWatchEventAsync(
                                elapsed,
                                $"tool {toolCall.Name} completed in " +
                                $"{(elapsed.Elapsed - toolCall.StartedAt).TotalMilliseconds:0} ms");
                        }
                    }
                }

                if (!string.IsNullOrEmpty(update.Text))
                {
                    await _output.WriteAsync(update.Text);
                    await _output.FlushAsync(timeoutSource.Token);
                }
            }

            await _output.WriteLineAsync();
            if (watch)
            {
                await WriteWatchEventAsync(
                    elapsed,
                    $"agent completed in {elapsed.Elapsed.TotalMilliseconds:0} ms");
            }
            _logger.LogInformation("Agent test turn completed successfully");
            return true;
        }
        catch (OperationCanceledException) when (
            timeoutSource.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            outcome = "timeout";
            errorType = typeof(TimeoutException).FullName;
            _logger.LogWarning("Agent test turn timed out after {Timeout}", _turnTimeout);
            await _error.WriteLineAsync(
                $"The agent request timed out after {_turnTimeout.TotalSeconds:0.###} seconds.");
            return false;
        }
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
            _logger.LogInformation("Agent test turn was cancelled");
            throw;
        }
        catch (Exception exception)
        {
            outcome = "error";
            errorType = exception.GetType().FullName;
            _logger.LogError(
                "Agent test turn failed with {ExceptionType}",
                exception.GetType().FullName);
            await _error.WriteLineAsync($"Agent request failed: {exception.Message}");
            return false;
        }
        finally
        {
            AITelemetry.RecordOperation(
                "agent.test.turn",
                "agent",
                agent.Name ?? "unnamed",
                outcome,
                elapsed.Elapsed,
                errorType,
                modelSelection.Provider,
                modelSelection.Model);
        }
    }

    private async Task WriteWatchEventAsync(Stopwatch elapsed, string message)
    {
        await _error.WriteLineAsync(
            $"[watch {elapsed.Elapsed:hh\\:mm\\:ss\\.fff}] {message}");
    }

    private static bool IsExitCommand(string input)
    {
        string command = input.Trim();
        return command.Equals("/exit", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("quit", StringComparison.OrdinalIgnoreCase);
    }
}
