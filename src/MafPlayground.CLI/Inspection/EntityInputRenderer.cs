using System.Text.Json;
using Microsoft.Extensions.AI;

namespace MafPlayground.CLI.Inspection;

internal sealed class EntityInputRenderer(TextWriter output)
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };

    public static JsonElement Schema(LocalEntityDescriptor descriptor) =>
        AIJsonUtilities.CreateJsonSchema(
            descriptor.InputType,
            $"Required input for {descriptor.Id}",
            hasDefaultValue: false,
            defaultValue: null,
            SerializerOptions);

    public async Task RenderAsync(
        LocalEntityDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        object document = new
        {
            entityId = descriptor.Id,
            entityType = descriptor.Kind.ToString().ToLowerInvariant(),
            inputType = descriptor.InputType.FullName,
            schema = Schema(descriptor),
            example = descriptor.InputExample,
        };

        await output.WriteLineAsync(JsonSerializer.Serialize(document, SerializerOptions));
        await output.FlushAsync(cancellationToken);
    }
}
