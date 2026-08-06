using System.Net;

namespace MafPlayground.CLI.DevUI;

internal static class DevUIEndpointPolicy
{
    public static Uri ValidateLoopback(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttp ||
            uri.UserInfo.Length > 0 ||
            uri.Port is < 1 or > 65_535 ||
            !IsLoopbackHost(uri.Host))
        {
            throw new ArgumentException(
                "DevUI must use a loopback-only HTTP URL such as http://localhost:5050. " +
                "Remote hosting requires a separate authenticated host.",
                nameof(value));
        }

        return uri;
    }

    private static bool IsLoopbackHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out IPAddress? address) &&
            IPAddress.IsLoopback(address);
    }
}
