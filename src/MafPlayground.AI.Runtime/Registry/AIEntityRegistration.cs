using System.Text.Json;
using MafPlayground.AI.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using MafPlayground.AI.Runtime.Agents;
using MafPlayground.AI.Runtime.Workflows;

namespace MafPlayground.AI.Runtime.Registry;

internal sealed record AIEntityRegistration(
    AIEntityDescriptor Descriptor,
    Type ImplementationType,
    JsonElement? InputExample,
    Func<IServiceProvider, AIEntityAvailability>? Availability,
    Func<IServiceProvider, AIEntityInspectionContext, IMafAgentSource>? AgentSourceFactory,
    Func<IServiceProvider, AIEntityInspectionContext, IMafWorkflowSource>? WorkflowSourceFactory,
    Func<IServiceProvider, AIEntityConstructionContext, object>? ProfileFactory,
    Func<object?, AIOutcomeClassification>? OutcomeClassifier,
    bool SupportsStreaming)
{
    public AIEntityRegistrationInfo Info => new(
        Descriptor, InputExample, AgentSourceFactory, WorkflowSourceFactory, ProfileFactory is not null, OutcomeClassifier is not null, SupportsStreaming);
}

public sealed class AIEntityRegistrationBuilder
{
    internal AIEntityRegistrationBuilder()
    {
    }

    internal string? Description { get; private set; }

    internal string? RuntimeOptionsSection { get; private set; }

    internal bool SupportsSessions { get; private set; }

    internal string? AuthorizationPolicy { get; private set; }

    internal bool AllowAnonymous { get; private set; }

    internal Type? InputExampleType { get; private set; }

    internal JsonElement? InputExample { get; private set; }

    internal Func<IServiceProvider, AIEntityAvailability>? Availability { get; private set; }

    internal Func<IServiceProvider, AIEntityInspectionContext, IMafAgentSource>? AgentSourceFactory { get; private set; }

    internal Func<IServiceProvider, AIEntityInspectionContext, IMafWorkflowSource>? WorkflowSourceFactory { get; private set; }

    internal Func<IServiceProvider, AIEntityConstructionContext, object>? ProfileFactory { get; private set; }
    internal Type? ProfileType { get; private set; }
    internal Func<object?, AIOutcomeClassification>? OutcomeClassifier { get; private set; }
    internal Type? ClassifierOutputType { get; private set; }
    internal bool SupportsStreaming { get; private set; }

    public AIEntityRegistrationBuilder WithProfileFactory<TEntity>(Func<IServiceProvider, AIEntityConstructionContext, TEntity> factory) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        ProfileType = typeof(TEntity);
        ProfileFactory = (services, context) => factory(services, context);
        return this;
    }

    public AIEntityRegistrationBuilder WithOutcomeClassifier<TOutput>(Func<TOutput, AIOutcomeClassification> classifier)
    {
        ArgumentNullException.ThrowIfNull(classifier);
        ClassifierOutputType = typeof(TOutput);
        OutcomeClassifier = output => classifier((TOutput)output!);
        return this;
    }

    /// <summary>The typed entity emits events through AIExecutionContext.Observer during its normal execution.</summary>
    public AIEntityRegistrationBuilder WithStreamingEvents() { SupportsStreaming = true; return this; }

    public AIEntityRegistrationBuilder WithInputExample<TInput>(TInput example)
    {
        ArgumentNullException.ThrowIfNull(example);
        InputExampleType = typeof(TInput);
        InputExample = JsonSerializer.SerializeToElement(example, AIJsonSerializerOptions.Default);
        return this;
    }

    /// <summary>Checks configuration only; callbacks must not construct an entity or invoke external services.</summary>
    public AIEntityRegistrationBuilder WithAvailability(Func<IServiceProvider, AIEntityAvailability> availability)
    {
        ArgumentNullException.ThrowIfNull(availability);
        Availability = availability;
        return this;
    }

    public AIEntityRegistrationBuilder WithMafAgentSource<TSource>() where TSource : class, IMafAgentSource =>
        WithMafAgentSource((services, _) => services.GetRequiredService<TSource>());

    public AIEntityRegistrationBuilder WithMafAgentSource(
        Func<IServiceProvider, AIEntityInspectionContext, IMafAgentSource> sourceFactory)
    {
        ArgumentNullException.ThrowIfNull(sourceFactory);
        AgentSourceFactory = sourceFactory;
        return this;
    }

    public AIEntityRegistrationBuilder WithMafWorkflowSource<TSource>() where TSource : class, IMafWorkflowSource =>
        WithMafWorkflowSource((services, _) => services.GetRequiredService<TSource>());

    public AIEntityRegistrationBuilder WithMafWorkflowSource(
        Func<IServiceProvider, AIEntityInspectionContext, IMafWorkflowSource> sourceFactory)
    {
        ArgumentNullException.ThrowIfNull(sourceFactory);
        WorkflowSourceFactory = sourceFactory;
        return this;
    }

    public AIEntityRegistrationBuilder WithDescription(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Description = description.Trim();
        return this;
    }

    public AIEntityRegistrationBuilder BindRuntimeOptions(string configurationSection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationSection);
        RuntimeOptionsSection = configurationSection.Trim();
        return this;
    }

    public AIEntityRegistrationBuilder EnableSessions()
    {
        SupportsSessions = true;
        return this;
    }

    public AIEntityRegistrationBuilder RequireAuthorization(string policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policy);
        AuthorizationPolicy = policy.Trim();
        AllowAnonymous = false;
        return this;
    }

    public AIEntityRegistrationBuilder AllowAnonymousAccess()
    {
        AuthorizationPolicy = null;
        AllowAnonymous = true;
        return this;
    }
}
