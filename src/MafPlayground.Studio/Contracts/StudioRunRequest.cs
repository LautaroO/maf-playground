using System.Text.Json;
using MafPlayground.AI.Abstractions;

namespace MafPlayground.Studio.Contracts;

public sealed record StudioRunRequest(string RequestId, string EntityId, string ProfileId, JsonElement Input, string? SessionId);
public sealed record StudioRunReceipt(string HostInstanceId, string RunId);
public sealed record StudioExecutionContext(string RunId, string EntityId, string ProfileId, string? SessionId, AICallerContext Caller);
