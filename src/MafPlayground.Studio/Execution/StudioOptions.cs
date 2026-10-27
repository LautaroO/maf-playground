namespace MafPlayground.Studio.Execution;

public sealed record StudioOptions
{
    public bool EnableCapture { get; init; } = true;
    public IReadOnlyList<string> Secrets { get; init; } = [];
    public string DefaultProfileId { get; init; } = "cli-default";
    public int MaxConcurrentRuns { get; init; } = 4;
    public int QueueCapacity { get; init; } = 16;
    public int MaxCompletedRuns { get; init; } = 100;
    public long MaxCaptureBytes { get; init; } = 128 * 1024 * 1024;
    public int MaxPayloadBytes { get; init; } = 2 * 1024 * 1024;
    public int MaxEventsPerRun { get; init; } = 2048;
    public int MaxRequestRecords { get; init; } = 10000;
    public long MaxMetadataBytes { get; init; } = 8 * 1024 * 1024;
    public TimeSpan RunTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan ShutdownGracePeriod { get; init; } = TimeSpan.FromSeconds(10);
}
