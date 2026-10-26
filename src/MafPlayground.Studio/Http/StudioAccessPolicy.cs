using System.Net;
using System.Security.Cryptography;
using System.Text;
using MafPlayground.AI.Abstractions;
using Microsoft.AspNetCore.Http;

namespace MafPlayground.Studio.Http;

/// <summary>One local host identity. Neither tokens nor bootstrap codes are accepted in URLs.</summary>
public sealed class StudioAccessPolicy
{
    public const string CookieName = "studio-access";
    private readonly string cookie = NewSecret();
    private int exchanged;
    public Uri Endpoint { get; }
    public string BootstrapCode { get; } = NewSecret();
    public string BearerToken { get; } = NewSecret();
    internal IReadOnlyList<string> Secrets => [BootstrapCode, BearerToken, cookie];
    public AICallerContext Caller { get; } = new("studio-local");

    public StudioAccessPolicy(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? endpoint) || endpoint.Scheme != "http" ||
            endpoint.UserInfo.Length != 0 || endpoint.AbsolutePath != "/" || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
            !(endpoint.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
              IPAddress.TryParse(endpoint.IdnHost.Trim('[', ']'), out IPAddress? address) && IPAddress.IsLoopback(address)))
            throw new ArgumentException("Studio requires an HTTP loopback URL without credentials, path, query, or fragment.", nameof(url));
        Endpoint = endpoint;
    }

    public bool IsLocalRequest(HttpContext context)
    {
        if (context.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address)) return false;
        if (!string.Equals(context.Request.Scheme, Endpoint.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(context.Request.Host.Value, Endpoint.Authority, StringComparison.OrdinalIgnoreCase)) return false;
        var origin = context.Request.Headers.Origin;
        return origin.Count == 0 || origin.Count == 1 && string.Equals(origin[0], Endpoint.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
    }

    public bool IsAuthenticated(HttpContext context)
    {
        if (!IsLocalRequest(context)) return false;
        return IsBearerAuthenticated(context) ||
            Volatile.Read(ref exchanged) == 1 && context.Request.Cookies.TryGetValue(CookieName, out string? value) && Equal(value, cookie);
    }

    public bool IsBearerAuthenticated(HttpContext context)
    {
        if (!IsLocalRequest(context)) return false;
        string? authorization = context.Request.Headers.Authorization.Count == 1 ? context.Request.Headers.Authorization[0] : null;
        return authorization is not null && authorization.StartsWith("Bearer ", StringComparison.Ordinal) && Equal(authorization[7..], BearerToken);
    }

    public bool TryExchange(string code, out string? value)
    {
        value = null;
        if (!Equal(code, BootstrapCode) || Interlocked.CompareExchange(ref exchanged, 1, 0) != 0) return false;
        value = cookie;
        return true;
    }

    private static bool Equal(string value, string secret) => value.Length == secret.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(value), Encoding.UTF8.GetBytes(secret));
    private static string NewSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
