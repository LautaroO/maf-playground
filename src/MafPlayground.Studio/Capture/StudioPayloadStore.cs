using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Execution;

namespace MafPlayground.Studio.Capture;

/// <summary>Normalizes values; the journal exclusively owns retention and byte accounting.</summary>
public sealed class StudioPayloadStore(StudioOptions options)
{
    private static readonly JsonSerializerOptions Serializer = new() { MaxDepth = 32, Converters = { new ExceptionConverter() } };
    private readonly JsonWriterOptions writerOptions = new() { Encoder = new StudioBoundedEncoder(options.MaxPayloadBytes) };
    internal StudioPayloadRedactor Redactor { get; } = new(options.Secrets, options.MaxPayloadBytes);

    public StudioPayload Capture(object? value, string contentType)
    {
        string id = Guid.NewGuid().ToString("N");
        try
        {
            BoundedBuffer raw = new(options.MaxPayloadBytes);
            using (Utf8JsonWriter writer = new(raw, writerOptions)) JsonSerializer.Serialize(writer, value, Serializer);
            using JsonDocument document = JsonDocument.Parse(raw.WrittenMemory, new() { MaxDepth = 32 });
            BoundedBuffer sanitized = new(options.MaxPayloadBytes);
            using (Utf8JsonWriter writer = new(sanitized, writerOptions)) Redactor.Write(writer, document.RootElement);
            using JsonDocument result = JsonDocument.Parse(sanitized.WrittenMemory);
            bool changed = !raw.WrittenMemory.Span.SequenceEqual(sanitized.WrittenMemory.Span);
            return new(id, contentType, result.RootElement.Clone(), changed ? StudioCaptureState.Redacted : StudioCaptureState.Available, changed ? "credentials_redacted" : null);
        }
        catch (PayloadLimitException)
        {
            return new(id, contentType, JsonSerializer.SerializeToElement(new { captureState = "truncated", reason = "payload_limit" }), StudioCaptureState.Truncated, "payload_limit");
        }
        catch (Exception)
        {
            // No exception message or user ToString is safe at this boundary.
            return new(id, contentType, JsonSerializer.SerializeToElement(new { captureState = "unavailable", reason = "serialization_failed" }), StudioCaptureState.Unavailable, "serialization_failed");
        }
    }

    private sealed class ExceptionConverter : JsonConverter<Exception>
    {
        public override bool CanConvert(Type typeToConvert) => typeof(Exception).IsAssignableFrom(typeToConvert);
        public override Exception Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, Exception value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("errorType", value.GetType().FullName);
            writer.WriteString("message", value.Message);
            writer.WriteEndObject();
        }
    }

    internal sealed class PayloadLimitException : Exception;
    /// <summary>
    /// Rejects oversized writer requests before allocating. Utf8JsonWriter reserves
    /// worst-case escaped space (six bytes per UTF-16 character) and grows in at
    /// least 4 KiB chunks. That fixed/configuration-bounded reserve is separate from
    /// the strict committed payload limit; neither grows with an oversized input.
    /// </summary>
    internal sealed class BoundedBuffer(int limit) : IBufferWriter<byte>
    {
        private readonly int allocationLimit = (int)Math.Min(int.MaxValue, Math.Max(4096L, 6L * limit));
        private byte[] buffer = [];
        private int written;
        internal ReadOnlyMemory<byte> WrittenMemory => buffer.AsMemory(0, written);

        public void Advance(int count)
        {
            if (count < 0 || count > buffer.Length - written) throw new ArgumentOutOfRangeException(nameof(count));
            if (count > limit - written) throw new PayloadLimitException();
            written += count;
        }
        public Memory<byte> GetMemory(int sizeHint = 0) { Reserve(sizeHint); return buffer.AsMemory(written); }
        public Span<byte> GetSpan(int sizeHint = 0) { Reserve(sizeHint); return buffer.AsSpan(written); }
        private void Reserve(int sizeHint)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
            sizeHint = Math.Max(1, sizeHint);
            if (sizeHint > allocationLimit - written) throw new PayloadLimitException();
            int required = written + sizeHint;
            if (required <= buffer.Length) return;
            int capacity = (int)Math.Min(allocationLimit, Math.Max(required, Math.Max(256L, 2L * buffer.Length)));
            Array.Resize(ref buffer, capacity);
        }
    }
}
