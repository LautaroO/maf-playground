using System.Net;
using MafPlayground.Studio.Http;
using Microsoft.AspNetCore.Http;

namespace MafPlayground.Studio.Tests.Unit;

public sealed class StudioAccessPolicyTests
{
    [Theory]
    [InlineData("http://example.com:5051")]
    [InlineData("http://0.0.0.0:5051")]
    [InlineData("http://user:password@localhost:5051")]
    [InlineData("http://localhost:5051/path")]
    [InlineData("http://localhost:5051/?token=x")]
    [InlineData("https://localhost:5051")]
    public void Rejects_unsafe_listeners(string url) => Assert.Throws<ArgumentException>(() => new StudioAccessPolicy(url));

    [Fact]
    public void Requires_exact_host_origin_loopback_and_credential()
    {
        var policy = new StudioAccessPolicy("http://localhost:5051");
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new("localhost", 5051);
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        Assert.False(policy.IsAuthenticated(context));
        context.Request.Headers.Authorization = "Bearer " + policy.BearerToken;
        Assert.True(policy.IsAuthenticated(context));
        context.Request.Headers.Origin = "http://foreign.test";
        Assert.False(policy.IsLocalRequest(context));
        context.Request.Headers.Origin = "http://localhost:5051";
        Assert.True(policy.IsLocalRequest(context));
        context.Request.Host = new("evil.test", 5051);
        Assert.False(policy.IsLocalRequest(context));
        context.Request.Host = new("localhost", 5051);
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
        Assert.False(policy.IsLocalRequest(context));
    }

    [Fact]
    public void Bootstrap_code_is_single_use_and_distinct_from_bearer()
    {
        var policy = new StudioAccessPolicy("http://localhost:5051");
        Assert.False(policy.TryExchange("wrong", out _));
        Assert.True(policy.TryExchange(policy.BootstrapCode, out string? cookie));
        Assert.NotEqual(policy.BearerToken, cookie);
        Assert.False(policy.TryExchange(policy.BootstrapCode, out _));
    }
}
