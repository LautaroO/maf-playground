using MafPlayground.AI.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MafPlayground.AI.AspNetCore;

internal sealed class AIAuthorizationStartupValidator(
    IServiceScopeFactory scopeFactory,
    IAuthorizationPolicyProvider policies) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IAIEntityRegistry registry = scope.ServiceProvider.GetRequiredService<IAIEntityRegistry>();
        foreach (AIEntityDescriptor entity in registry.Entities.Where(item => !item.AllowAnonymous))
        {
            AuthorizationPolicy? policy = await policies.GetPolicyAsync(
                entity.AuthorizationPolicy!).ConfigureAwait(false);
            if (policy is null)
            {
                throw new InvalidOperationException(
                    $"Authorization policy '{entity.AuthorizationPolicy}' for AI entity " +
                    $"'{entity.Id}' is not registered.");
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
