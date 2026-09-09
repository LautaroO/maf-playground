using System.Security.Claims;
using System.Text.Encodings.Web;
using MafPlayground.AI;
using MafPlayground.AI.AspNetCore;
using MafPlayground.AI.Context;
using MafPlayground.AI.Contracts;
using MafPlayground.AI.Workflows.Translation;
using MafPlayground.Observability;
using MafPlayground.Providers.Google;
using MafPlayground.Providers.Ollama;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    EnvironmentName = Environments.Development,
});
builder.Services
    .AddAuthentication(DevelopmentAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
        DevelopmentAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AI.Basic", policy => policy.RequireClaim("ai_permission", "basic"));
    options.AddPolicy(
        "AI.Translation",
        policy => policy.RequireClaim("ai_permission", "translation"));
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IUserContextAccessor, HttpUserContextAccessor>();
builder.Services.AddAIRuntime(builder.Configuration)
    .AddMafPlaygroundSampleEntities();
builder.Services.AddInMemoryAgentSessions();
builder.Services.AddAIEndpoints(builder.Configuration);
builder.Services.Configure<TranslationWorkflowOptions>(
    builder.Configuration.GetSection("AI:Workflows:Translation"));
builder.Services
    .AddGoogleGenAIProvider(builder.Configuration)
    .AddOllamaProvider(builder.Configuration)
    .AddMafPlaygroundObservability(builder.Configuration);

WebApplication app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapAIEntities();
app.MapGet("/", () => Results.Ok(new
{
    service = "MafPlayground ASP.NET Core sample",
    endpoint = "/api/ai/entities/{entityId}/runs",
}));
await app.RunAsync();

internal sealed class HttpUserContextAccessor(IHttpContextAccessor httpContextAccessor)
    : IUserContextAccessor
{
    public UserContext GetCurrent()
    {
        ClaimsPrincipal? user = httpContextAccessor.HttpContext?.User;
        return new UserContext(user?.Claims
            .Where(claim => claim.Type is "sub" or "tenant_id")
            .Select(claim => new KeyValuePair<string, string>(claim.Type, claim.Value))
            .ToArray() ?? []);
    }
}

internal sealed class DevelopmentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevelopmentHeaders";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string subject = Request.Headers["X-AI-Subject"].ToString();
        if (string.IsNullOrWhiteSpace(subject))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        List<Claim> claims = [new("sub", subject)];
        string tenant = Request.Headers["X-AI-Tenant"].ToString();
        if (!string.IsNullOrWhiteSpace(tenant))
        {
            claims.Add(new Claim("tenant_id", tenant));
        }
        foreach (string permission in Request.Headers["X-AI-Permissions"]
            .ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            claims.Add(new Claim("ai_permission", permission));
        }

        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, SchemeName)));
    }
}
