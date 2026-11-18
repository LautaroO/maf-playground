using System.CommandLine;
using MafPlayground.CLI.Commands;
using MafPlayground.Retrieval;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Tests;

public sealed class ServeCommandTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Retrieval_composition_requires_explicit_studio_opt_in(bool enabled)
    {
        var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(Path.GetDirectoryName(typeof(ServeCommand).Assembly.Location)!, "appsettings.json")).AddInMemoryCollection(new Dictionary<string, string?> { ["Studio:EnableRetrieval"] = enabled.ToString() }).Build();
        var services = new ServiceCollection();
        ServeCommand.AddOptionalRetrieval(services, config);
        Assert.Equal(enabled, services.Any(service => service.ServiceType == typeof(IKnowledgeSearchFactory)));
    }

    [Fact]
    public async Task Startup_provenance_is_optional_sanitized_and_does_not_require_git()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var absent = await StudioSourceRevision.ReadAsync(directory, default, (_, _) => throw new Exception("Must not invoke Git outside checkout"));
            Assert.Null(absent.Revision); Assert.Null(absent.Dirty);
            File.WriteAllText(Path.Combine(directory, "MafPlayground.slnx"), "fixture");
            File.WriteAllText(Path.Combine(directory, ".git"), "fixture");
            var available = await StudioSourceRevision.ReadAsync(directory, default, (command, _) => Task.FromResult<string?>(command.StartsWith("rev-parse", StringComparison.Ordinal) ? new string('a', 40) : " M source.cs"));
            Assert.Equal(new string('a', 40), available.Revision); Assert.True(available.Dirty);
            var invalid = await StudioSourceRevision.ReadAsync(directory, default, (_, _) => Task.FromResult<string?>("private invalid metadata"));
            Assert.Null(invalid.Revision); Assert.Null(invalid.Dirty);
            var unavailable = await StudioSourceRevision.ReadAsync(directory, default, (_, _) => throw new System.ComponentModel.Win32Exception());
            Assert.Null(unavailable.Revision);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Maps_options_and_defaults()
    {
        ServeCommandOptions? captured = null;
        Command command = ServeCommand.Create((options, _) => { captured = options; return Task.FromResult(0); });
        Assert.Equal(0, await command.Parse(["--model", "fake:chat", "--url", "http://localhost:6070"]).InvokeAsync());
        Assert.Equal(new ServeCommandOptions("fake:chat", "http://localhost:6070"), captured);
        Assert.Equal(0, await command.Parse([]).InvokeAsync());
        Assert.Equal(new ServeCommandOptions(null, "http://localhost:5051"), captured);
    }
}
