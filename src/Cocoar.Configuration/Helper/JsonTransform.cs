using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Cocoar.Configuration.Helper;

internal static class JsonTransform
{
    private readonly struct Segment
    {
        public readonly byte[]? NameUtf8; // when property name
        public readonly int Index;        // when array index
        public readonly bool IsIndex;

        public Segment(byte[] nameUtf8)
        {
            NameUtf8 = nameUtf8;
            Index = -1;
            IsIndex = false;
        }

        public Segment(int index)
        {
            NameUtf8 = null;
            Index = index;
            IsIndex = true;
        }
    }

    public static byte[] SelectAndMount(ReadOnlyMemory<byte> input, string? selectPath, string? mountPath)
    {
        if (string.IsNullOrWhiteSpace(selectPath))
        {
            if (string.IsNullOrWhiteSpace(mountPath))
            {
                return input.ToArray();
            }

            var segments = ParseMountSegments(mountPath);
            var buffer = new ArrayBufferWriter<byte>(input.Length + segments.Count * 32);
            using (var writer = new Utf8JsonWriter(buffer))
            {
                for (int i = 0; i < segments.Count; i++)
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName(segments[i]);
                }
                writer.Flush();
            }
            buffer.Write(input.Span);
            for (int i = 0; i < segments.Count; i++)
            {
                buffer.GetSpan(1)[0] = (byte)'}';
                buffer.Advance(1);
            }

            return buffer.WrittenSpan.ToArray();
        }
        var data = input.Span;
        if (TryGetSelectedSlice(data, ParseSelectSegments(selectPath!), out var start, out var length))
        {
            var selectedSlice = data.Slice(start, length);
            if (string.IsNullOrWhiteSpace(mountPath))
            {
                return selectedSlice.ToArray();
            }

            var segments = ParseMountSegments(mountPath!);
            var buffer = new ArrayBufferWriter<byte>(selectedSlice.Length + segments.Count * 32);
            using (var writer = new Utf8JsonWriter(buffer))
            {
                for (int i = 0; i < segments.Count; i++)
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName(segments[i]);
                }
                writer.Flush();
            }

            buffer.Write(selectedSlice);
            for (int i = 0; i < segments.Count; i++)
            {
                buffer.GetSpan(1)[0] = (byte)'}';
                buffer.Advance(1);
            }
            return buffer.WrittenSpan.ToArray();
        }
        // Note: This builds a JsonDocument only to select/mount and immediately writes bytes back.
        // It does not materialize user payload values as strings, and it does not leak DOM across layers.
        // Providers and RuleManager still operate on bytes; parsing for merge happens in the Orchestrator.
        using var doc = JsonDocument.Parse(input);
        var element = doc.RootElement;
        element = JsonHelper.SelectColonDelimited(element, selectPath!);
        if (!string.IsNullOrWhiteSpace(mountPath))
        {
            element = JsonHelper.WrapIfNeeded(element, mountPath!);
        }
        var domBuffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(domBuffer))
        {
            element.WriteTo(writer);
            writer.Flush();
        }
        return domBuffer.WrittenSpan.ToArray();
    }

    private static List<byte[]> ParseMountSegments(string mountPath)
    {
        var list = new List<byte[]>();
        var parts = mountPath.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in parts)
        {
            list.Add(Encoding.UTF8.GetBytes(p));
        }
        return list;
    }

    private static List<Segment> ParseSelectSegments(string selectPath)
    {
        var list = new List<Segment>();
        var parts = selectPath.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in parts)
        {
            if (int.TryParse(p, out var idx) && idx >= 0)
            {
                list.Add(new Segment(idx));
            }
            else
            {
                list.Add(new Segment(Encoding.UTF8.GetBytes(p)));
            }
        }
        return list;
    }

    private static bool TryGetSelectedSlice(ReadOnlySpan<byte> data, List<Segment> segs, out int start, out int length)
    {
        start = length = 0;
        var reader = new Utf8JsonReader(data, isFinalBlock: true, state: default);
        if (!reader.Read()) return false;

        return FindAtCurrent(ref reader, data, segs, 0, out start, out length);
    }

    private static bool FindAtCurrent(ref Utf8JsonReader reader, ReadOnlySpan<byte> data, List<Segment> segs, int segIndex, out int start, out int length)
    {
        if (segIndex >= segs.Count)
        {
            var s = checked((int)reader.TokenStartIndex);
            if (!reader.TrySkip()) { start = length = 0; return false; }
            var e = checked((int)reader.BytesConsumed);
            start = s; length = e - s; return true;
        }

        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
                return FindInObject(ref reader, data, segs, segIndex, out start, out length);
            case JsonTokenType.StartArray:
                return FindInArray(ref reader, data, segs, segIndex, out start, out length);
            default:
                start = length = 0; return false;
        }
    }

    private static bool FindInObject(ref Utf8JsonReader reader, ReadOnlySpan<byte> data, List<Segment> segs, int segIndex, out int start, out int length)
    {
        start = length = 0;
        if (segIndex >= segs.Count || segs[segIndex].IsIndex) return false;

        var target = segs[segIndex].NameUtf8!;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.ValueSpan;
                var isMatch = name.SequenceEqual(target);
                if (!reader.Read()) return false; // move to value

                if (isMatch)
                {
                    if (FindAtCurrent(ref reader, data, segs, segIndex + 1, out start, out length))
                    {
                        return true;
                    }
                    if (!reader.TrySkip()) return false;
                }
                else
                {
                    if (!reader.TrySkip()) return false;
                }
            }
            else if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }
        }
        return false;
    }

    private static bool FindInArray(ref Utf8JsonReader reader, ReadOnlySpan<byte> data, List<Segment> segs, int segIndex, out int start, out int length)
    {
        start = length = 0;
        if (segIndex >= segs.Count || !segs[segIndex].IsIndex) return false;
        var desired = segs[segIndex].Index;
        var idx = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) break;

            if (idx == desired)
            {
                if (FindAtCurrent(ref reader, data, segs, segIndex + 1, out start, out length))
                {
                    return true;
                }
                if (!reader.TrySkip()) return false;
            }
            else
            {
                if (!reader.TrySkip()) return false;
            }
            idx++;
        }
        return false;
    }
}
