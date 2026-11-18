namespace MafPlayground.CLI.Tui;

internal sealed partial class RemoteWorkspace
{
    private void Replay()
    {
        if (_run is null) { Notice("Open a run to replay its retained journal."); return; }
        GraphInspectionDialog.Replay(GraphEvidence.From(_run));
    }

    private void CompareRuns(string baselineId, string candidateId)
    {
        Request(async () =>
        {
            var comparison = await _client.CompareAsync(_instance, baselineId, candidateId, _token).ConfigureAwait(false);
            var journals = await Task.WhenAll(ReadComparisonJournal(baselineId), ReadComparisonJournal(candidateId)).ConfigureAwait(false);
            return (comparison, baseline: GraphEvidence.From(journals[0]), candidate: GraphEvidence.From(journals[1]));
        }, result =>
        {
            var matches = result.comparison.Field("invocations").Items().ToLookup(m => m.Str("executorId"));
            InvocationPair[] Matched(string node) => node == result.baseline.RunNode || node == result.candidate.RunNode
                ? GraphInspectionDialog.Pair(result.baseline, result.candidate, node)
                : matches[node].Select(match =>
                new InvocationPair((int)match.Number("occurrence"),
                    result.baseline.Calls.GetValueOrDefault(node)?.FirstOrDefault(i => i.Id == match.Str("baselineInvocationId")),
                    result.candidate.Calls.GetValueOrDefault(node)?.FirstOrDefault(i => i.Id == match.Str("candidateInvocationId")))).ToArray();
            GraphInspectionDialog.Compare(result.baseline, result.candidate, result.comparison.Pretty(), Matched,
                result.comparison.Field("truncated").ValueKind == System.Text.Json.JsonValueKind.True);
        });
    }

    private async Task<RemoteJournal> ReadComparisonJournal(string id)
    {
        var snapshot = await _client.RunAsync(id, _token).ConfigureAwait(false);
        if (snapshot.Str("hostInstanceId") != _instance) throw new InvalidDataException("The Studio instance changed.");
        var journal = new RemoteJournal(id);
        // Match the host comparison's first 2048 retained events per run.
        int remaining = 2048;
        do
        {
            var page = await _client.EventPageAsync(id, journal.Cursor, Math.Min(remaining, 128), _token).ConfigureAwait(false);
            long previous = journal.Cursor;
            journal.Apply(snapshot, page);
            int count = page.Field("events").GetArrayLength();
            remaining -= count;
            if (count == 0 || journal.Cursor <= previous) break;
        } while (remaining > 0 && journal.Cursor < snapshot.Number("latestSequence"));
        return journal;
    }
}
