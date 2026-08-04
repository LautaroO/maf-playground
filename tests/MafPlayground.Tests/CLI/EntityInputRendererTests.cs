using System.Text.Json;
using MafPlayground.CLI.Inspection;

namespace MafPlayground.Tests;

public sealed class EntityInputRendererTests
{
    [Fact]
    public async Task RenderAsync_WorkflowIncludesTypedSchemaAndExample()
    {
        LocalEntityDescriptor descriptor = Assert.Single(
            LocalEntityCatalog.All,
            entity => entity.Id == "translation-workflow");
        StringWriter output = new();
        EntityInputRenderer renderer = new(output);

        await renderer.RenderAsync(descriptor);

        using JsonDocument document = JsonDocument.Parse(output.ToString());
        JsonElement root = document.RootElement;
        Assert.Equal("translation-workflow", root.GetProperty("entityId").GetString());
        Assert.Equal("object", root.GetProperty("schema").GetProperty("type").GetString());
        Assert.True(root.GetProperty("schema").GetProperty("properties").TryGetProperty(
            "text",
            out _));
        Assert.Equal(
            "Hello, how are you?",
            root.GetProperty("example").GetProperty("text").GetString());
    }

    [Fact]
    public async Task RenderAsync_AgentDeclaresConversationalStringInput()
    {
        LocalEntityDescriptor descriptor = Assert.Single(
            LocalEntityCatalog.All,
            entity => entity.Id == "basic-rag-agent");
        StringWriter output = new();

        await new EntityInputRenderer(output).RenderAsync(descriptor);

        using JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.Equal(
            "string",
            document.RootElement.GetProperty("schema").GetProperty("type").GetString());
    }
}
