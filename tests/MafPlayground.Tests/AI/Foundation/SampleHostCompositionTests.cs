using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Runtime.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MafPlayground.Tests.AI.Foundation;

public sealed class SampleHostCompositionTests
{
    [Fact]
    public async Task DevelopmentExample_BuildsWithOnlyItsOriginalEntityDependencies()
    {
        // Observe the real entry point and stop immediately after Build, before sockets or model calls.
        using BuildCapture capture = new();
        using IDisposable subscription = DiagnosticListener.AllListeners.Subscribe(capture);
        MethodInfo entryPoint = Assembly.Load("MafPlayground.AspNetCore").EntryPoint!;
        await Assert.ThrowsAsync<BuildCompletedException>(() => Task.Run(async () =>
        {
            capture.IsTarget.Value = true;
            try
            {
                if (entryPoint.Invoke(null, [new[] { "--environment=Development" }]) is Task run) await run;
            }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            }
        }).WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.True(capture.ValidationEnabled);
        IHost host = Assert.IsAssignableFrom<IHost>(capture.Host);
        using IServiceScope scope = host.Services.CreateScope();
        IAIEntityRegistry registry = scope.ServiceProvider.GetRequiredService<IAIEntityRegistry>();
        IAIEntityInspection inspection = scope.ServiceProvider.GetRequiredService<IAIEntityInspection>();
        Assert.Equal(["basic-agent", "translation-workflow"], registry.Entities.Select(entity => entity.Id));
        foreach (AIEntityDescriptor entity in registry.Entities)
        {
            Assert.True(inspection.GetAvailability(entity.Id).IsAvailable);
            Assert.NotNull(inspection.GetRegistration(entity.Id).InputExample);
            Assert.NotNull(await host.Services.GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync(entity.AuthorizationPolicy!));
        }
        Assert.NotNull(inspection.GetAgentSource("basic-agent", new("example-default")));
        Assert.NotNull(inspection.GetWorkflowSource("translation-workflow", new("example-default"))!.CreateWorkflow());
    }

    private sealed class BuildCompletedException : Exception;

    private sealed class BuildCapture : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly ConcurrentBag<IDisposable> subscriptions = [];
        internal AsyncLocal<bool> IsTarget { get; } = new();
        internal IHost? Host { get; private set; }
        internal bool ValidationEnabled { get; private set; }
        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "Microsoft.Extensions.Hosting") subscriptions.Add(listener.Subscribe(this));
        }
        public void OnNext(KeyValuePair<string, object?> item)
        {
            if (!IsTarget.Value) return;
            if (item.Key == "HostBuilding" && item.Value is IHostBuilder builder)
            {
                builder.UseDefaultServiceProvider(options =>
                {
                    options.ValidateOnBuild = true;
                    options.ValidateScopes = true;
                    ValidationEnabled = true;
                });
            }
            if (item.Key == "HostBuilt" && item.Value is IHost host)
            {
                Host = host;
                throw new BuildCompletedException();
            }
        }
        public void OnCompleted() { }
        public void OnError(Exception error) => throw error;
        public void Dispose()
        {
            foreach (IDisposable subscription in subscriptions) subscription.Dispose();
            Host?.Dispose();
        }
    }
}
