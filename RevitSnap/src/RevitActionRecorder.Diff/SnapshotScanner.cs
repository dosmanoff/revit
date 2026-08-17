using System.Buffers;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace RevitActionRecorder.Diff;

/// <summary>
/// Потоковый обход снапшота: по верхнеуровневым свойствам корневого объекта.
/// Элементы «больших» массивов отдаются по одному сырым JSON-текстом и тут же
/// забываются — файл на 300 МБ разбирается без построения дерева в памяти.
/// </summary>
internal static class SnapshotScanner
{
    /// <param name="elementSections">Разделы-массивы, которые отдаются поэлементно.</param>
    /// <param name="onElement">(секция, сырой JSON элемента) — вызывается на каждый элемент.</param>
    /// <param name="onOtherSection">(имя, сырой JSON значения) — прочие (небольшие) разделы целиком.</param>
    public static void Scan(
        string path,
        ISet<string> elementSections,
        Action<string, string> onElement,
        Action<string, string> onOtherSection)
    {
        using var file = File.OpenRead(path);
        using Stream stream = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? new GZipStream(file, CompressionMode.Decompress)
            : file;
        Scan(stream, elementSections, onElement, onOtherSection);
    }

    public static void Scan(
        Stream stream,
        ISet<string> elementSections,
        Action<string, string> onElement,
        Action<string, string> onOtherSection)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            int dataLength = 0;
            bool endOfStream = false;
            var state = new JsonReaderState(new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });

            // Позиция обхода: 0 — до корня, 1 — внутри корня, 2 — внутри целевого массива.
            int depthMode = 0;
            string? currentSection = null;
            bool inElementArray = false;

            while (true)
            {
                if (!endOfStream)
                {
                    int read = stream.Read(buffer, dataLength, buffer.Length - dataLength);
                    if (read == 0) endOfStream = true;
                    else dataLength += read;
                }

                var consumed = Process(
                    buffer.AsSpan(0, dataLength), endOfStream, ref state,
                    ref depthMode, ref currentSection, ref inElementArray,
                    elementSections, onElement, onOtherSection, out bool needMoreData);

                if (consumed > 0)
                {
                    var leftover = dataLength - consumed;
                    if (leftover > 0)
                        Buffer.BlockCopy(buffer, consumed, buffer, 0, leftover);
                    dataLength = leftover;
                }

                if (needMoreData)
                {
                    if (endOfStream && consumed == 0)
                        throw new JsonException("Unexpected end of snapshot JSON.");
                    // Значение целиком не помещается в буфер — растим буфер и дочитываем.
                    if (dataLength == buffer.Length)
                    {
                        var bigger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                        Buffer.BlockCopy(buffer, 0, bigger, 0, dataLength);
                        ArrayPool<byte>.Shared.Return(buffer);
                        buffer = bigger;
                    }
                    continue;
                }

                if (endOfStream && dataLength == 0)
                    break;
                if (endOfStream && consumed == 0)
                    break;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static int Process(
        ReadOnlySpan<byte> span,
        bool isFinalBlock,
        ref JsonReaderState state,
        ref int depthMode,
        ref string? currentSection,
        ref bool inElementArray,
        ISet<string> elementSections,
        Action<string, string> onElement,
        Action<string, string> onOtherSection,
        out bool needMoreData)
    {
        var reader = new Utf8JsonReader(span, isFinalBlock, state);
        needMoreData = false;

        while (true)
        {
            long safePoint = reader.BytesConsumed;
            var localState = reader.CurrentState;

            if (!reader.Read())
            {
                if (!isFinalBlock) needMoreData = true;
                state = localState;
                return (int)safePoint;
            }

            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject when depthMode == 0:
                    depthMode = 1;
                    break;

                case JsonTokenType.PropertyName when depthMode == 1:
                    currentSection = reader.GetString();
                    break;

                case JsonTokenType.StartArray when depthMode == 1 && currentSection is not null
                                                   && elementSections.Contains(currentSection):
                    depthMode = 2;
                    inElementArray = true;
                    break;

                case JsonTokenType.EndArray when depthMode == 2:
                    depthMode = 1;
                    inElementArray = false;
                    break;

                case JsonTokenType.StartObject when depthMode == 2 && inElementArray:
                {
                    long start = reader.TokenStartIndex;
                    if (!reader.TrySkip())
                    {
                        needMoreData = true;
                        state = localState;
                        return (int)safePoint;
                    }
                    var slice = span[(int)start..(int)reader.BytesConsumed];
                    onElement(currentSection!, Encoding.UTF8.GetString(slice));
                    break;
                }

                case JsonTokenType.StartObject or JsonTokenType.StartArray when depthMode == 1:
                {
                    long start = reader.TokenStartIndex;
                    if (!reader.TrySkip())
                    {
                        needMoreData = true;
                        state = localState;
                        return (int)safePoint;
                    }
                    var slice = span[(int)start..(int)reader.BytesConsumed];
                    onOtherSection(currentSection ?? "", Encoding.UTF8.GetString(slice));
                    break;
                }

                case JsonTokenType.EndObject when depthMode == 1:
                    depthMode = 0;
                    state = reader.CurrentState;
                    return (int)reader.BytesConsumed;
            }
        }
    }
}
