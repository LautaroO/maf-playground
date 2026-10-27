using System.Text.Encodings.Web;

namespace MafPlayground.Studio.Capture;

/// <summary>
/// Utf8JsonWriter can allocate escaping scratch space before requesting destination
/// memory. Reject oversized text at that earlier boundary, then preserve the
/// framework's default encoding exactly. Pointer signatures are required by the
/// encoder API; this wrapper never dereferences or modifies a pointer.
/// </summary>
internal sealed class StudioBoundedEncoder(int limit) : JavaScriptEncoder
{
    public override int MaxOutputCharactersPerInputCharacter => Default.MaxOutputCharactersPerInputCharacter;
    public override bool WillEncode(int unicodeScalar) => Default.WillEncode(unicodeScalar);
    public override unsafe int FindFirstCharacterToEncode(char* text, int textLength)
    {
        Check(textLength);
        return Default.FindFirstCharacterToEncode(text, textLength);
    }
    public override int FindFirstCharacterToEncodeUtf8(ReadOnlySpan<byte> utf8Text)
    {
        Check(utf8Text.Length);
        return Default.FindFirstCharacterToEncodeUtf8(utf8Text);
    }
    public override unsafe bool TryEncodeUnicodeScalar(int unicodeScalar, char* buffer, int bufferLength, out int numberOfCharactersWritten) =>
        Default.TryEncodeUnicodeScalar(unicodeScalar, buffer, bufferLength, out numberOfCharactersWritten);
    private void Check(int length)
    {
        if (length > limit) throw new StudioPayloadStore.PayloadLimitException();
    }
}
