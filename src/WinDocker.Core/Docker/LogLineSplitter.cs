using System.Buffers;
using System.Globalization;
using System.Text;
using WinDocker.Core.Models;

namespace WinDocker.Core.Docker;

/// <summary>
/// Turns the byte chunks of a Docker log stream into <see cref="LogLine"/>s. Stdout and stderr each keep
/// their own UTF-8 decoder and partial line, so multi-byte characters and lines that span chunks survive.
/// Expects the RFC 3339 timestamp prefix that the engine adds when timestamps are requested.
/// </summary>
public sealed class LogLineSplitter
{
    /// <summary>Longest line kept in memory; a longer line is emitted in pieces.</summary>
    internal const int MaxLineLength = 64 * 1024;

    private readonly StreamState stdout = new();
    private readonly StreamState stderr = new();

    public IReadOnlyList<LogLine> Append(ReadOnlySpan<byte> chunk, bool isError)
    {
        if (chunk.IsEmpty)
        {
            return [];
        }

        var state = isError ? stderr : stdout;
        var buffer = ArrayPool<char>.Shared.Rent(state.Decoder.GetCharCount(chunk, flush: false));
        try
        {
            var written = state.Decoder.GetChars(chunk, buffer, flush: false);
            return Split(state, buffer.AsSpan(0, written), isError);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    /// <summary>Emits the trailing partial line of each stream. Call once the stream has ended.</summary>
    public IReadOnlyList<LogLine> Flush()
    {
        List<LogLine>? lines = null;
        FlushStream(stdout, isError: false, ref lines);
        FlushStream(stderr, isError: true, ref lines);
        return lines ?? [];
    }

    private static List<LogLine> Split(StreamState state, ReadOnlySpan<char> text, bool isError)
    {
        List<LogLine> lines = [];
        while (true)
        {
            var newline = text.IndexOf('\n');
            if (newline < 0)
            {
                state.Pending.Append(text);
                if (state.Pending.Length > MaxLineLength)
                {
                    lines.Add(TakePendingLine(state, isError));
                }

                return lines;
            }

            if (state.Pending.Length == 0)
            {
                lines.Add(CreateLine(text[..newline], isError));
            }
            else
            {
                state.Pending.Append(text[..newline]);
                lines.Add(TakePendingLine(state, isError));
            }

            text = text[(newline + 1)..];
        }
    }

    private static void FlushStream(StreamState state, bool isError, ref List<LogLine>? lines)
    {
        // Bytes of an incomplete multi-byte character decode to the replacement character.
        var leftover = state.Decoder.GetCharCount([], flush: true);
        if (leftover > 0)
        {
            Span<char> buffer = stackalloc char[leftover];
            state.Decoder.GetChars([], buffer, flush: true);
            state.Pending.Append(buffer);
        }

        state.Decoder.Reset();
        if (state.Pending.Length > 0)
        {
            (lines ??= []).Add(TakePendingLine(state, isError));
        }
    }

    private static LogLine TakePendingLine(StreamState state, bool isError)
    {
        var line = state.Pending.ToString();
        state.Pending.Clear();
        return CreateLine(line, isError);
    }

    private static LogLine CreateLine(ReadOnlySpan<char> line, bool isError)
    {
        if (line.EndsWith('\r'))
        {
            line = line[..^1];
        }

        var separator = line.IndexOf(' ');
        var token = separator < 0 ? line : line[..separator];
        if (!TryParseTimestamp(token, out var timestamp))
        {
            return new LogLine(null, line.ToString(), isError);
        }

        var text = separator < 0 ? [] : line[(separator + 1)..];
        return new LogLine(timestamp, text.ToString(), isError);
    }

    /// <summary>
    /// Parses <c>yyyy-MM-ddTHH:mm:ss[.fffffffff](Z|+hh:mm|-hh:mm)</c>. Fractions beyond the 100 ns
    /// resolution of <see cref="DateTimeOffset"/> are truncated, so nanosecond timestamps parse.
    /// </summary>
    internal static bool TryParseTimestamp(ReadOnlySpan<char> text, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (text.Length < 20 || text[4] != '-' || text[7] != '-' || text[10] != 'T' || text[13] != ':' || text[16] != ':')
        {
            return false;
        }

        if (!TryParseDigits(text[..4], out var year)
            || !TryParseDigits(text.Slice(5, 2), out var month)
            || !TryParseDigits(text.Slice(8, 2), out var day)
            || !TryParseDigits(text.Slice(11, 2), out var hour)
            || !TryParseDigits(text.Slice(14, 2), out var minute)
            || !TryParseDigits(text.Slice(17, 2), out var second))
        {
            return false;
        }

        var index = 19;
        var fractionTicks = 0L;
        if (text[index] == '.')
        {
            index++;
            var start = index;
            var scale = TimeSpan.TicksPerSecond / 10;
            while (index < text.Length && char.IsAsciiDigit(text[index]))
            {
                fractionTicks += (text[index] - '0') * scale;
                scale /= 10;
                index++;
            }

            if (index == start)
            {
                return false;
            }
        }

        var zone = text[index..];
        TimeSpan offset;
        if (zone is "Z")
        {
            offset = TimeSpan.Zero;
        }
        else if (zone.Length == 6
            && zone[0] is '+' or '-'
            && zone[3] == ':'
            && TryParseDigits(zone.Slice(1, 2), out var offsetHours)
            && TryParseDigits(zone.Slice(4, 2), out var offsetMinutes))
        {
            offset = new TimeSpan(offsetHours, offsetMinutes, 0);
            if (zone[0] == '-')
            {
                offset = -offset;
            }
        }
        else
        {
            return false;
        }

        try
        {
            timestamp = new DateTimeOffset(year, month, day, hour, minute, second, offset).AddTicks(fractionTicks);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryParseDigits(ReadOnlySpan<char> text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private sealed class StreamState
    {
        public Decoder Decoder { get; } = Encoding.UTF8.GetDecoder();

        public StringBuilder Pending { get; } = new();
    }
}
