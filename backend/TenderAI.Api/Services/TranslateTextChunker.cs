using System.Text;

namespace TenderAI.Api.Services
{
    public static class TranslateTextChunker
    {
        public const int MaxUtf8Bytes = 10_000;

        public static IReadOnlyList<string> Split(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            if (text.Length == 0)
            {
                return [];
            }

            var chunks = new List<string>();
            var start = 0;
            var index = 0;
            var byteCount = 0;
            var lastWhitespaceBoundary = -1;

            while (index < text.Length)
            {
                var charCount = char.IsHighSurrogate(text[index])
                    && index + 1 < text.Length
                    && char.IsLowSurrogate(text[index + 1])
                    ? 2
                    : 1;
                var unitBytes = Encoding.UTF8.GetByteCount(text.AsSpan(index, charCount));

                if (byteCount + unitBytes > MaxUtf8Bytes)
                {
                    var boundary = lastWhitespaceBoundary > start ? lastWhitespaceBoundary : index;
                    chunks.Add(text[start..boundary]);
                    start = boundary;
                    index = boundary;
                    byteCount = 0;
                    lastWhitespaceBoundary = -1;
                    continue;
                }

                byteCount += unitBytes;
                index += charCount;
                if (char.IsWhiteSpace(text[index - 1]))
                {
                    lastWhitespaceBoundary = index;
                }
            }

            chunks.Add(text[start..]);
            return chunks;
        }
    }
}
