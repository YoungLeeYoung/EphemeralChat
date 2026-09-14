using System.Buffers.Text;

namespace EphemeralChat.Security.Primitives;

/// <summary>
/// RFC 4648 Base32 encoder (uppercase alphabet, optional '=' padding).
/// Decoding is intentionally not implemented — nothing needs it yet.
/// </summary>
public static class Base32Encoder
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data, bool padding = false)
    {
        int outputLength = (data.Length * 8 + 4) / 5;
        int paddedLength = (outputLength + 7) / 8 * 8;
        char[] result = new char[padding ? paddedLength : outputLength];

        int bitBuffer = 0;
        int bitsInBuffer = 0;
        int index = 0;

        foreach (byte b in data)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitsInBuffer += 8;

            while (bitsInBuffer >= 5)
            {
                bitsInBuffer -= 5;
                result[index++] = Alphabet[(bitBuffer >> bitsInBuffer) & 0x1F];
            }
        }

        if (bitsInBuffer > 0)
        {
            result[index++] = Alphabet[(bitBuffer << (5 - bitsInBuffer)) & 0x1F];
        }

        if (padding)
        {
            while (index < result.Length)
            {
                result[index++] = '=';
            }
        }

        return new string(result);
    }
}
