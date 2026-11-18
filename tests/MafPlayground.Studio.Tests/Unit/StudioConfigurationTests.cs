using MafPlayground.Studio.Execution;
using MafPlayground.Studio.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Studio.Tests.Unit;

public sealed class StudioConfigurationTests
{
    [Fact]
    public void Profiles_parse_safe_model_selectors_and_expose_only_allowlisted_ids()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Studio:Profiles:0:Id"] = "small", ["Studio:Profiles:0:Model"] = "local:family/model:tag", ["Studio:Profiles:0:GuardProfile"] = "Default" }).Build();
        var services = new ServiceCollection().AddStudio(config);
        using var provider = services.BuildServiceProvider();
        var profile = Assert.Single(provider.GetServices<StudioExecutionProfile>());
        Assert.Equal("local:family/model:tag", profile.Model!.ToString());
        var catalog = provider.GetRequiredService<StudioProfileCatalog>();
        Assert.Contains("small", catalog.Ids);
        Assert.Throws<ArgumentException>(() => catalog.Validate("arbitrary:model"));
    }

    [Fact]
    public void Default_model_configuration_is_subject_to_the_same_safe_selector_policy()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["AI:Defaults:Model"] = "provider:model?token=private" }).Build();
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddStudio(config));
    }

    [Theory]
    [InlineData(63)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MaxValue - 4096)]
    public void Payload_limit_leaves_safe_space_for_envelope_and_overflow_probe(int maximum)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Studio:Limits:MaxPayloadBytes"] = maximum.ToString(System.Globalization.CultureInfo.InvariantCulture) }).Build();
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddStudio(config));
    }

    [Theory]
    [InlineData("provider:https://user:password@host/model")]
    [InlineData("provider:model?token=secret")]
    [InlineData("provider:model with spaces")]
    public void Profiles_reject_credential_or_url_shaped_selectors(string model)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Studio:Profiles:0:Id"] = "unsafe", ["Studio:Profiles:0:Model"] = model }).Build();
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddStudio(config));
    }
}
