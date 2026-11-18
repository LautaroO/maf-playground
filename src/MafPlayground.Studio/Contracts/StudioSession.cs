namespace MafPlayground.Studio.Contracts;

public sealed record StudioSessionRequest(string EntityId, string ProfileId);
public sealed record StudioSession(string Id, string EntityId, string ProfileId, DateTimeOffset CreatedAt);
