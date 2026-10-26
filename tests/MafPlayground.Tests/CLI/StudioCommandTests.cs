using System.CommandLine;
using System.Text.Json;
using MafPlayground.CLI.Commands;
using MafPlayground.Retrieval;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Tests;

public sealed class StudioCommandTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Retrieval_composition_requires_explicit_studio_opt_in(bool enabled)
    {
        var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(Path.GetDirectoryName(typeof(StudioCommand).Assembly.Location)!, "appsettings.json")).AddInMemoryCollection(new Dictionary<string, string?> { ["Studio:EnableRetrieval"] = enabled.ToString() }).Build();
        var services = new ServiceCollection();
        StudioCommand.AddOptionalRetrieval(services, config);
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
        StudioCommandOptions? captured = null;
        Command command = StudioCommand.Create((options, _) => { captured = options; return Task.FromResult(0); });
        Assert.Equal(0, await command.Parse(["--model", "fake:chat", "--url", "http://localhost:6070", "--no-open", "--export-contracts", "contracts.json"]).InvokeAsync());
        Assert.Equal(new StudioCommandOptions("fake:chat", "http://localhost:6070", true, "contracts.json"), captured);
        Assert.Equal(0, await command.Parse([]).InvokeAsync());
        Assert.Equal(new StudioCommandOptions(null, "http://localhost:5051", false, null), captured);
    }

    [Fact]
    public async Task Offline_export_requires_no_model_or_host_and_includes_transport_and_entity_schemas()
    {
        string path = Path.Combine(Path.GetTempPath(), $"studio-{Guid.NewGuid():N}.json");
        try
        {
            Assert.Equal(0, await StudioCommand.Create().Parse(["--export-contracts", path, "--model", "invalid", "--url", "invalid"]).InvokeAsync());
            using JsonDocument schema = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.True(schema.RootElement.GetProperty("transport").TryGetProperty("StudioRunRequest", out _));
            Assert.Equal(4, schema.RootElement.GetProperty("entities").GetArrayLength());
        }
        finally { File.Delete(path); }
    }
}
