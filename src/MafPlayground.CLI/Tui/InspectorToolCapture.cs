using System.Diagnostics;
using Microsoft.Agents.AI;

namespace MafPlayground.CLI.Tui;

internal static class InspectorToolCapture
{
    public static AIAgent Attach(AIAgent agent, Action<Action<InspectorRun>> publish) =>
        FunctionInvocationDelegatingAgentBuilderExtensions.Use(agent.AsBuilder(),
            async (_, context, next, token) =>
            {
                string id = "tool/" + InspectorRun.Clean(context.Function.Name);
                var invocation = new Invocation(InspectorRun.Format(context.Arguments))
                {
                    CallId = InspectorRun.Clean(context.CallContent.CallId),
                };
                publish(run => run.BeginTool(id, invocation));
                try
                {
                    object? result = await next(context, token).ConfigureAwait(false);
                    long finishedAt = Stopwatch.GetTimestamp();
                    string output = InspectorRun.Format(result);
                    publish(run => run.Finish(id, invocation, output, "", finishedAt));
                    return result;
                }
                catch (Exception error)
                {
                    long finishedAt = Stopwatch.GetTimestamp();
                    publish(run => run.Finish(id, invocation, "", error.Message, finishedAt));
                    throw;
                }
            }).Build();
}
