using System.Text;
using Unity.Collections;

/// <summary>
/// FixedString conversion that trims by UTF-8 byte length, so Korean or emoji text
/// never overflows the fixed buffer.
/// </summary>
public static class NetText
{
    public static FixedString64Bytes Trim64(string text)
    {
        return new FixedString64Bytes(TrimToBytes(text, FixedString64Bytes.UTF8MaxLengthInBytes));
    }

    public static FixedString512Bytes Trim512(string text)
    {
        return new FixedString512Bytes(TrimToBytes(text, FixedString512Bytes.UTF8MaxLengthInBytes));
    }

    static string TrimToBytes(string text, int maxBytes)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        while (text.Length > 0 && Encoding.UTF8.GetByteCount(text) > maxBytes)
        {
            text = text.Substring(0, text.Length - 1);
        }

        return text;
    }
}
