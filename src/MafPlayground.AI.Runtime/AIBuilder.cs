using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Runtime.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.AI.Runtime;

public sealed class AIBuilder
{
    private readonly HashSet<string> _entityIds = new(StringComparer.OrdinalIgnoreCase);

    internal AIBuilder(IServiceCollection services)
    {
        Services = services;
    }

    public IServiceCollection Services { get; }

    /// <summary>Metadata from normal registrations, available to host composition before building DI.</summary>
    public IReadOnlyList<AIEntityRegistrationInfo> Registrations => AIEntityRegistry.BuildMap(Services
            .Select(service => service.ImplementationInstance)
            .OfType<AIEntityRegistration>())
        .Values.Select(registration => registration.Info)
        .ToArray();

    public AIBuilder AddAgent<TEntity, TInput, TOutput>(
        string id,
        Action<AIEntityRegistrationBuilder> configure)
        where TEntity : class, IAIEntity<TInput, TOutput> =>
        Add<TEntity, TInput, TOutput>(id, AIEntityKind.Agent, configure);

    public AIBuilder AddWorkflow<TEntity, TInput, TOutput>(
        string id,
        Action<AIEntityRegistrationBuilder> configure)
        where TEntity : class, IAIEntity<TInput, TOutput> =>
        Add<TEntity, TInput, TOutput>(id, AIEntityKind.Workflow, configure);

    private AIBuilder Add<TEntity, TInput, TOutput>(
        string id,
        AIEntityKind kind,
        Action<AIEntityRegistrationBuilder> configure)
        where TEntity : class, IAIEntity<TInput, TOutput>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(configure);
        id = id.Trim();
        if (id.Length > 100 || id.Any(character =>
                !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
        {
            throw new ArgumentException(
                "AI entity IDs may contain only letters, digits, hyphens, underscores, and periods.",
                nameof(id));
        }
        if (!_entityIds.Add(id))
        {
            throw new InvalidOperationException(
                $"AI entity ID '{id}' is registered more than once.");
        }

        AIEntityRegistrationBuilder builder = new();
        configure(builder);
        if (string.IsNullOrWhiteSpace(builder.Description))
        {
            throw new InvalidOperationException($"AI entity '{id}' requires a description.");
        }
        if (!builder.AllowAnonymous && string.IsNullOrWhiteSpace(builder.AuthorizationPolicy))
        {
            throw new InvalidOperationException(
                $"AI entity '{id}' requires an authorization policy or an explicit anonymous override.");
        }
        if (builder.AllowAnonymous && builder.SupportsSessions)
        {
            throw new InvalidOperationException(
                $"AI entity '{id}' cannot combine anonymous access with sessions.");
        }
        if (builder.RuntimeOptionsSection is not null &&
            !string.Equals(
                builder.RuntimeOptionsSection,
                $"AI:Entities:{id}",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"AI entity '{id}' runtime options must bind to 'AI:Entities:{id}'.");
        }

        if (builder.InputExampleType is not null && builder.InputExampleType != typeof(TInput))
        {
            throw new InvalidOperationException($"AI entity '{id}' example must use its registered input contract.");
        }
        if ((kind != AIEntityKind.Agent && builder.AgentSourceFactory is not null) ||
            (kind != AIEntityKind.Workflow && builder.WorkflowSourceFactory is not null))
        {
            throw new InvalidOperationException($"AI entity '{id}' native source must match its registered kind.");
        }

        AIEntityDescriptor descriptor = new(
            id,
            kind,
            builder.Description,
            typeof(TInput),
            typeof(TOutput),
            builder.SupportsSessions,
            builder.AuthorizationPolicy,
            builder.AllowAnonymous);
        Services.AddScoped<TEntity>();
        Services.AddSingleton(new AIEntityRegistration(
            descriptor,
            typeof(TEntity),
            builder.InputExample,
            builder.Availability,
            builder.AgentSourceFactory,
            builder.WorkflowSourceFactory));
        return this;
    }
}
