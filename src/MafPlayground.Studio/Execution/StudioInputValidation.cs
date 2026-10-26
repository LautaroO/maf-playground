using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MafPlayground.AI.Runtime.Registry;

namespace MafPlayground.Studio.Execution;

internal static class StudioInputValidation
{
    private static readonly JsonSerializerOptions Json = new(AIJsonSerializerOptions.Default)
    { RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true };

    internal static void Validate(JsonElement input, Type type)
    {
        object value = input.Deserialize(type, Json) ?? throw new JsonException();
        Check(value, Json.GetTypeInfo(type), null, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
    }

    private static void Check(object? value, JsonTypeInfo contract, NullabilityInfo? nullable, HashSet<object> seen, int depth)
    {
        if (depth > 64) throw new JsonException();
        if (value is null)
        {
            if (nullable?.ReadState == NullabilityState.NotNull) throw new JsonException();
            return;
        }
        if (!value.GetType().IsValueType && !seen.Add(value)) return;
        // A custom converter owns its representation and invariants; do not impose reflection shape on it.
        if (contract.Kind == JsonTypeInfoKind.Object)
        {
            foreach (var property in contract.Properties)
            {
                if (property.Get is null) continue;
                var info = property.AttributeProvider is PropertyInfo member ? new NullabilityInfoContext().Create(member) : null;
                Check(property.Get(value), Json.GetTypeInfo(property.PropertyType), info, seen, depth + 1);
            }
        }
        else if (contract.Kind == JsonTypeInfoKind.Dictionary && value is IEnumerable entries)
        {
            var valueInfo = nullable?.GenericTypeArguments.ElementAtOrDefault(1);
            foreach (object entry in entries)
            {
                object? item = entry is DictionaryEntry dictionaryEntry ? dictionaryEntry.Value : entry.GetType().GetProperty("Value")?.GetValue(entry);
                if (item is null && valueInfo?.ReadState == NullabilityState.NotNull) throw new JsonException();
                if (item is not null) Check(item, Json.GetTypeInfo(item.GetType()), valueInfo, seen, depth + 1);
            }
        }
        else if (contract.Kind == JsonTypeInfoKind.Enumerable && value is IEnumerable items)
        {
            NullabilityInfo? itemInfo = nullable?.ElementType ?? nullable?.GenericTypeArguments.FirstOrDefault();
            foreach (object? item in items)
            {
                if (item is null && itemInfo?.ReadState == NullabilityState.NotNull) throw new JsonException();
                if (item is not null) Check(item, Json.GetTypeInfo(item.GetType()), itemInfo, seen, depth + 1);
            }
        }
    }
}
