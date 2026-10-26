using System.Text.Json;
using MafPlayground.CLI.Inspection;
using MafPlayground.AI;
using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Tests;

public sealed class EntityInputRendererTests
{
    [Fact]
    public async Task Catalog_UsesNormalRegistrationsForNovelInputsAndExamples()
    {
        AIBuilder builder = new ServiceCollection().ConfigureAIEntities();
        builder.AddAgent<NovelInspectionEntity, NovelInspectionInput, string>("novel-inspection", entity => entity
            .WithDescription("Novel inspection entity").AllowAnonymousAccess()
            .WithInputExample(new NovelInspectionInput(["one", "two"])));
        LocalEntityDescriptor descriptor = Assert.Single(
            LocalEntityCatalog.Create(builder.Registrations), entity => entity.Id == "novel-inspection");
        StringWriter output = new();

        await new EntityInputRenderer(output).RenderAsync(descriptor);

        using JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.Equal("array", document.RootElement.GetProperty("schema").GetProperty("properties")
            .GetProperty("labels").GetProperty("type").GetString());
        Assert.Equal("two", document.RootElement.GetProperty("example").GetProperty("labels")[1].GetString());
    }

    [Fact]
    public void Catalog_ListsCurrentRegisteredAndLegacyEntitiesOnce()
    {
        AIBuilder builder = new ServiceCollection().ConfigureAIEntities().AddMafPlaygroundSampleEntities();
        Assert.Equal(["basic-agent", "basic-rag-agent", "repository-help-agent", "translation-workflow"],
            LocalEntityCatalog.Create(builder.Registrations).Select(entity => entity.Id).Order());
    }

    [Fact]
    public async Task RenderAsync_WorkflowIncludesTypedSchemaAndExample()
    {
        LocalEntityDescriptor descriptor = Assert.Single(
            LocalEntityCatalog.Create(new ServiceCollection().ConfigureAIEntities().AddMafPlaygroundSampleEntities().Registrations),
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
            LocalEntityCatalog.Create(new ServiceCollection().ConfigureAIEntities().AddMafPlaygroundSampleEntities().Registrations),
            entity => entity.Id == "basic-rag-agent");
        StringWriter output = new();

        await new EntityInputRenderer(output).RenderAsync(descriptor);

        using JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.Equal(
            "string",
            document.RootElement.GetProperty("schema").GetProperty("type").GetString());
    }

    public sealed record NovelInspectionInput(string[] Labels);

    public sealed class NovelInspectionEntity : IAIEntity<NovelInspectionInput, string>
    {
        public ValueTask<string> ExecuteAsync(NovelInspectionInput input, AIExecutionContext context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Inspection must not execute entities.");
    }
}
