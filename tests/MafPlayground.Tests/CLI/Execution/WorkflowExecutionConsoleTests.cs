using MafPlayground.CLI;
using MafPlayground.CLI.Execution;
using Microsoft.Agents.AI.Workflows;

namespace MafPlayground.Tests;

public sealed class WorkflowExecutionConsoleTests
{
    [Fact]
    public async Task RenderAsync_PrintsExecutorLifecycleWithoutPayload()
    {
        StringWriter output = new();
        WorkflowExecutionConsole console = new(output);

        await console.RenderAsync(new ExecutorInvokedEvent("translate-es", "private input"));
        await console.RenderAsync(new ExecutorCompletedEvent("translate-es", "private output"));

        string text = output.ToString();
        Assert.Contains("▶ translate-es", text, StringComparison.Ordinal);
        Assert.Contains("✓ translate-es", text, StringComparison.Ordinal);
        Assert.DoesNotContain("private input", text, StringComparison.Ordinal);
        Assert.DoesNotContain("private output", text, StringComparison.Ordinal);
    }
}
