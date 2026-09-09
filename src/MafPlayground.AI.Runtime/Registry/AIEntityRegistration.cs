using MafPlayground.AI.Abstractions;

namespace MafPlayground.AI.Runtime.Registry;

internal sealed record AIEntityRegistration(
    AIEntityDescriptor Descriptor,
    Type ImplementationType);

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
