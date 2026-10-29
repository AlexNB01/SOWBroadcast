namespace SowBroadcast.Core.Replay;

/// <summary>Reads what the program needs to know about an MP4 file without a media library: how long it is.</summary>
public static class Mp4Info
{
    private const int MaxMoovBytes = 64 * 1024 * 1024;

    /// <summary>The length of the clip in milliseconds (from the <c>mvhd</c> box), or 0 when it cannot be read (a clip still being written, a fragmented file).</summary>
    public static long ReadDurationMs(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var moov = FindBox(stream, 0, stream.Length, "moov");
            if (moov is null) return 0;
            var (start, end) = moov.Value;
            var mvhd = FindBox(stream, start, end, "mvhd");
            if (mvhd is null) return 0;
            stream.Position = mvhd.Value.Start;
            var head = new byte[32];
            var read = stream.Read(head, 0, head.Length);
            return ParseMvhd(head.AsSpan(0, read));
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>The duration in a <c>mvhd</c> payload (version, flags, times, timescale, duration).</summary>
    public static long ParseMvhd(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 20) return 0;
        var version = payload[0];
        long timescale, duration;
        if (version == 1)
        {
            if (payload.Length < 32) return 0;
            timescale = ReadU32(payload, 20);
            duration = (long)ReadU64(payload, 24);
        }
        else
        {
            timescale = ReadU32(payload, 12);
            duration = ReadU32(payload, 16);
        }
        if (timescale <= 0 || duration <= 0) return 0;
        return (long)Math.Round(duration * 1000.0 / timescale);
    }

    // The payload range of the first box of this type between two offsets (boxes are size + type + payload).
    private static (long Start, long End)? FindBox(FileStream stream, long from, long to, string type)
    {
        var header = new byte[16];
        var position = from;
        while (position + 8 <= to)
        {
            stream.Position = position;
            if (stream.Read(header, 0, 8) < 8) return null;
            long size = ReadU32(header, 0);
            var name = System.Text.Encoding.ASCII.GetString(header, 4, 4);
            long headerSize = 8;
            if (size == 1)
            {
                if (stream.Read(header, 8, 8) < 8) return null;
                size = (long)ReadU64(header, 8);
                headerSize = 16;
            }
            else if (size == 0) size = to - position;
            if (size < headerSize) return null;

            var end = Math.Min(position + size, to);
            if (name == type)
            {
                if (end - (position + headerSize) > MaxMoovBytes) return null;
                return (position + headerSize, end);
            }
            // moov is searched for at the top level only; mvhd is the first box inside it.
            position += size;
        }
        return null;
    }

    private static long ReadU32(ReadOnlySpan<byte> b, int offset) =>
        ((long)b[offset] << 24) | ((long)b[offset + 1] << 16) | ((long)b[offset + 2] << 8) | b[offset + 3];

    private static ulong ReadU64(ReadOnlySpan<byte> b, int offset) =>
        ((ulong)ReadU32(b, offset) << 32) | (ulong)ReadU32(b, offset + 4);
}
