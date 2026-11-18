using MafPlayground.Studio.Contracts;

namespace MafPlayground.Studio.Execution;

// All access is serialized by StudioRunStore.Gate. Subscribers hold no private queue.
internal sealed class StudioEventJournal(int capacity)
{
    private readonly Queue<(StudioEvent Event, int Bytes)> events = new();
    internal long LatestSequence { get; private set; }
    internal long EarliestSequence => events.TryPeek(out var first) ? first.Event.Sequence : LatestSequence + 1;
    internal long Bytes { get; private set; }
    internal bool HasLostEvents { get; private set; }
    internal TaskCompletionSource Changed { get; private set; } = NewSignal();

    internal long NextSequence() => ++LatestSequence;

    internal void Append(StudioEvent item, int bytes)
    {
        events.Enqueue((item, bytes));
        Bytes += bytes;
        while (events.Count > capacity) RemoveOldest();
        Pulse();
    }

    internal void Drop()
    {
        HasLostEvents = true;
        Pulse();
    }

    internal int RemoveOldest()
    {
        if (!events.TryDequeue(out var first)) return 0;
        Bytes -= first.Bytes;
        HasLostEvents = true;
        return first.Bytes;
    }

    internal StudioEventPage Read(long after, int limit)
    {
        StudioEvent[] page = events.Where(item => item.Event.Sequence > after).Take(limit).Select(item => item.Event).ToArray();
        bool gap = after < EarliestSequence - 1;
        long previous = Math.Max(after, EarliestSequence - 1);
        foreach (StudioEvent item in page)
        {
            if (item.Sequence != previous + 1) gap = true;
            previous = item.Sequence;
        }
        // A dropped tail also advances the cursor; clients must fetch the snapshot.
        long next = page.Length == limit ? page[^1].Sequence : Math.Max(after, LatestSequence);
        if (page.Length < limit && next > previous) gap = true;
        return new(Array.AsReadOnly(page), next, EarliestSequence, gap);
    }

    internal void Pulse()
    {
        TaskCompletionSource old = Changed;
        Changed = NewSignal();
        old.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
