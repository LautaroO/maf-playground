using System.Text.Json;

namespace MafPlayground.Studio.Contracts;

public enum StudioCaptureState { Available, Redacted, Truncated, Unavailable }

/// <summary>A sanitized, bounded value. Payloads are retained only by the run journal.</summary>
public sealed record StudioPayload(string Id, string ContentType, JsonElement Value, StudioCaptureState State, string? Reason);
