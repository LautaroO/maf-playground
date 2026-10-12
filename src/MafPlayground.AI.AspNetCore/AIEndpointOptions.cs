namespace MafPlayground.AI.AspNetCore;

public sealed class AIEndpointOptions
{
    public const string ConfigurationSectionName = "AI:Http";

    public string SubjectClaimType { get; set; } = "sub";

    public string TenantClaimType { get; set; } = "tenant_id";

    public long MaxRequestBodyBytes { get; set; } = 1_048_576;
}
