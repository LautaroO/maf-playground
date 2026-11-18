namespace MafPlayground.CLI.Tui;

internal sealed partial class RemoteWorkspace
{
    private Task _catalogPoll = Task.CompletedTask;
    private DateTimeOffset _nextCatalogPoll;
    private bool _catalogPolling;

    private void PollCatalog()
    {
        if (_catalog is not { } catalog || _catalogPolling || DateTimeOffset.UtcNow < _nextCatalogPoll) return;
        _catalogPolling = true;
        string profile = _draft.Profile;
        _catalogPoll = BackgroundAsync(async () =>
        {
            var snapshots = await _client.ActivityAsync(_token).ConfigureAwait(false);
            return CatalogRun.FromStudio(snapshots, _instance, profile, DateTimeOffset.UtcNow, OpenEntity).ToArray();
        }, runs =>
        {
            _catalogPolling = false;
            if (_catalog != catalog) return;
            catalog.SetActivity(runs);
            _nextCatalogPoll = DateTimeOffset.UtcNow.AddSeconds(3);
        }, _ =>
        {
            _catalogPolling = false;
            if (_catalog != catalog) return;
            catalog.SetHistoryMessage("Run history offline · retrying");
            _nextCatalogPoll = DateTimeOffset.UtcNow.AddSeconds(10);
        });
    }
}
