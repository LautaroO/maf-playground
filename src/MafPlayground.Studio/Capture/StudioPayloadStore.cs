using System.Text.Json;
using System.Text.Json.Serialization;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Execution;

namespace MafPlayground.Studio.Capture;

/// <summary>Normalizes values; the journal exclusively owns retention and byte accounting.</summary>
public sealed class StudioPayloadStore(StudioOptions options)
{
    private static readonly JsonSerializerOptions Serializer = new() { MaxDepth = 32, Converters = { new ExceptionConverter() } };
    internal StudioPayloadRedactor Redactor { get; } = new(options.Secrets, options.MaxPayloadBytes);

    public StudioPayload Capture(object? value, string contentType)
    {
        string id = Guid.NewGuid().ToString("N");
        try
        {
            using BoundedStream raw = new(options.MaxPayloadBytes);
            JsonSerializer.Serialize(raw, value, Serializer);
            using JsonDocument document = JsonDocument.Parse(raw.ToArray(), new() { MaxDepth = 32 });
            using BoundedStream sanitized = new(options.MaxPayloadBytes);
            using (Utf8JsonWriter writer = new(sanitized)) Redactor.Write(writer, document.RootElement);
            byte[] bytes = sanitized.ToArray();
            using JsonDocument result = JsonDocument.Parse(bytes);
            bool changed = !raw.ToArray().AsSpan().SequenceEqual(bytes);
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

    private sealed class PayloadLimitException : Exception;
    internal sealed class BoundedStream(int limit) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
        private void Check(int count) { if (Length + count > limit) throw new PayloadLimitException(); }
    }
}
