using System.Text.Json;
using MafPlayground.AI.Abstractions;

namespace MafPlayground.Studio.Contracts;

public sealed record StudioEntityDescriptor(
    string Id,
    AIEntityKind Kind,
    string Description,
    string InputType,
    string OutputType,
    JsonElement InputSchema,
    JsonElement OutputSchema,
    JsonElement? InputExample,
    bool SupportsSessions,
    bool SupportsNativeAgent,
    bool SupportsNativeWorkflow,
    bool IsAvailable,
    string? UnavailableReason);
