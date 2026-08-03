namespace MafPlayground.CLI.DevUI;

internal sealed class DevUITraceResponseStream(
    Stream inner,
    DevUITraceSink sink,
    Func<bool> canEmitTraceEvents) : Stream
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => inner.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        _writeLock.Wait();
        try
        {
            DrainTraceEvents();
            inner.Flush();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await DrainTraceEventsAsync(cancellationToken);
            await inner.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _writeLock.Wait();
        try
        {
            sink.ObserveResponseChunk(buffer.AsSpan(offset, count));
            DrainTraceEvents();
            inner.Write(buffer, offset, count);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            sink.ObserveResponseChunk(buffer.Span);
            await DrainTraceEventsAsync(cancellationToken);
            await inner.WriteAsync(buffer, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await DrainTraceEventsAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _writeLock.Dispose();
        }

        base.Dispose(disposing);
    }

    private void DrainTraceEvents()
    {
        if (!canEmitTraceEvents())
        {
            return;
        }

        while (sink.TryDequeueFrame(out byte[]? frame))
        {
            inner.Write(frame!);
        }
    }

    private async Task DrainTraceEventsAsync(CancellationToken cancellationToken)
    {
        if (!canEmitTraceEvents())
        {
            return;
        }

        while (sink.TryDequeueFrame(out byte[]? frame))
        {
            await inner.WriteAsync(frame, cancellationToken);
        }
    }
}
