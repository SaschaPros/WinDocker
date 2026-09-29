using System.Text;
using WinDocker.Core.Docker;
using WinDocker.Core.Models;

namespace WinDocker.Core.Tests.Docker;

public class LogLineSplitterTests
{
    private const string Stamp = "2026-09-29T10:15:30.123456789Z";

    private static readonly DateTimeOffset StampInstant = new DateTimeOffset(2026, 9, 29, 10, 15, 30, TimeSpan.Zero).AddTicks(1_234_567);

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static List<LogLine> AppendAll(LogLineSplitter splitter, IEnumerable<byte[]> chunks, bool isError = false)
    {
        var lines = new List<LogLine>();
        foreach (var chunk in chunks)
        {
            lines.AddRange(splitter.Append(chunk, isError));
        }

        lines.AddRange(splitter.Flush());
        return lines;
    }

    [Fact]
    public void Append_ParsesTheTimestampPrefixWithNanoseconds()
    {
        var lines = new LogLineSplitter().Append(Utf8($"{Stamp} hello world\n"), isError: false);

        var line = Assert.Single(lines);
        Assert.Equal(StampInstant, line.Timestamp);
        Assert.Equal("hello world", line.Text);
        Assert.False(line.IsError);
    }

    [Fact]
    public void Append_ReturnsEveryCompleteLineOfAChunk()
    {
        var lines = new LogLineSplitter().Append(Utf8($"{Stamp} one\n{Stamp} two\n{Stamp} three\n"), isError: false);

        Assert.Equal(["one", "two", "three"], lines.Select(line => line.Text));
    }

    [Fact]
    public void Append_KeepsAnIncompleteLineUntilItsNewlineArrives()
    {
        var splitter = new LogLineSplitter();

        Assert.Empty(splitter.Append(Utf8($"{Stamp} par"), isError: false));
        Assert.Empty(splitter.Append(Utf8("tial"), isError: false));
        var line = Assert.Single(splitter.Append(Utf8(" line\n"), isError: false));

        Assert.Equal("partial line", line.Text);
        Assert.Equal(StampInstant, line.Timestamp);
    }

    [Fact]
    public void Append_ReturnsNothingForAnEmptyChunk() =>
        Assert.Empty(new LogLineSplitter().Append([], isError: false));

    [Fact]
    public void Append_YieldsTheSameLinesWhereverTheChunksAreCut()
    {
        var input = Utf8($"{Stamp} first line\n{Stamp} secondé line\r\n{Stamp} third");
        var expected = new[] { "first line", "secondé line", "third" };

        for (var cut = 0; cut <= input.Length; cut++)
        {
            var lines = AppendAll(new LogLineSplitter(), [input[..cut], input[cut..]]);

            Assert.True(expected.SequenceEqual(lines.Select(line => line.Text)), $"cut at byte {cut}");
            Assert.All(lines, line => Assert.Equal(StampInstant, line.Timestamp));
        }
    }

    [Fact]
    public void Append_KeepsMultiByteCharactersThatAreSplitAcrossChunks()
    {
        var input = Utf8($"{Stamp} héllo wörld € \U0001F433\n");

        var lines = AppendAll(new LogLineSplitter(), input.Select(value => new[] { value }));

        var line = Assert.Single(lines);
        Assert.Equal("héllo wörld € \U0001F433", line.Text);
        Assert.Equal(StampInstant, line.Timestamp);
    }

    [Fact]
    public void Append_RemovesTheCarriageReturnOfWindowsLineEndings()
    {
        var lines = new LogLineSplitter().Append(Utf8($"{Stamp} tty-out\r\n{Stamp} tty-err\r\n"), isError: false);

        Assert.Equal(["tty-out", "tty-err"], lines.Select(line => line.Text));
    }

    [Fact]
    public void Append_RemovesACarriageReturnThatIsSeparatedFromItsNewline()
    {
        var splitter = new LogLineSplitter();

        Assert.Empty(splitter.Append(Utf8($"{Stamp} tty-out\r"), isError: false));
        var line = Assert.Single(splitter.Append(Utf8("\n"), isError: false));

        Assert.Equal("tty-out", line.Text);
    }

    [Fact]
    public void Append_MarksStderrLines()
    {
        var splitter = new LogLineSplitter();

        var stdout = Assert.Single(splitter.Append(Utf8($"{Stamp} out\n"), isError: false));
        var stderr = Assert.Single(splitter.Append(Utf8($"{Stamp} err\n"), isError: true));

        Assert.False(stdout.IsError);
        Assert.True(stderr.IsError);
    }

    [Fact]
    public void Append_KeepsSeparatePartialLinesPerStream()
    {
        var splitter = new LogLineSplitter();

        Assert.Empty(splitter.Append(Utf8($"{Stamp} out-par"), isError: false));
        var stderr = Assert.Single(splitter.Append(Utf8($"{Stamp} err\n"), isError: true));
        var stdout = Assert.Single(splitter.Append(Utf8("tial\n"), isError: false));

        Assert.Equal(("err", true), (stderr.Text, stderr.IsError));
        Assert.Equal(("out-partial", false), (stdout.Text, stdout.IsError));
    }

    [Theory]
    [InlineData("2026-09-29T10:15:30Z", 0)]
    [InlineData("2026-09-29T10:15:30.5Z", 5_000_000)]
    [InlineData("2026-09-29T10:15:30.123Z", 1_230_000)]
    [InlineData("2026-09-29T10:15:30.1234567Z", 1_234_567)]
    [InlineData("2026-09-29T10:15:30.123456789Z", 1_234_567)]
    [InlineData("2026-09-29T10:15:30.999999999Z", 9_999_999)]
    public void Append_ParsesFractionsOfAnyLengthAndTruncatesToTicks(string stamp, long fractionTicks)
    {
        var line = Assert.Single(new LogLineSplitter().Append(Utf8($"{stamp} msg\n"), isError: false));

        Assert.Equal(new DateTimeOffset(2026, 9, 29, 10, 15, 30, TimeSpan.Zero).AddTicks(fractionTicks), line.Timestamp);
        Assert.Equal("msg", line.Text);
    }

    [Fact]
    public void Append_ParsesTimestampsWithAnOffset()
    {
        var lines = new LogLineSplitter().Append(
            Utf8("2026-09-29T12:15:30.123456789+02:00 east\n2026-09-29T05:15:30-05:00 west\n"),
            isError: false);

        var instant = new DateTimeOffset(2026, 9, 29, 10, 15, 30, TimeSpan.Zero);
        Assert.Equal(instant.AddTicks(1_234_567), lines[0].Timestamp);
        Assert.Equal(TimeSpan.FromHours(2), lines[0].Timestamp!.Value.Offset);
        Assert.Equal(instant, lines[1].Timestamp);
        Assert.Equal(TimeSpan.FromHours(-5), lines[1].Timestamp!.Value.Offset);
    }

    [Theory]
    [InlineData("hello world")]
    [InlineData("2026-09-29 10:15:30 space instead of T")]
    [InlineData("2026-13-40T99:99:99Z impossible date")]
    [InlineData("2026-09-29T10:15:30 missing zone")]
    [InlineData("2026-09-29T10:15:30.Z empty fraction")]
    [InlineData("2026-09-29T10:15:30+0200 zone without colon")]
    [InlineData("2026-09-29 date only")]
    [InlineData("  2026-09-29T10:15:30Z leading space")]
    public void Append_KeepsTheFullTextWhenThereIsNoValidTimestamp(string text)
    {
        var line = Assert.Single(new LogLineSplitter().Append(Utf8(text + "\n"), isError: false));

        Assert.Null(line.Timestamp);
        Assert.Equal(text, line.Text);
    }

    [Fact]
    public void Append_KeepsSpacesAndTabsAfterTheTimestamp()
    {
        var line = Assert.Single(new LogLineSplitter().Append(Utf8($"{Stamp}   indented\tand  spaced \n"), isError: false));

        Assert.Equal("  indented\tand  spaced ", line.Text);
    }

    [Fact]
    public void Append_ReturnsEmptyTextForAnEmptyMessage()
    {
        var lines = new LogLineSplitter().Append(Utf8($"{Stamp} \n\n"), isError: false);

        Assert.Equal(2, lines.Count);
        Assert.Equal((StampInstant, string.Empty), (lines[0].Timestamp, lines[0].Text));
        Assert.Equal((null, string.Empty), (lines[1].Timestamp, lines[1].Text));
    }

    [Fact]
    public void Flush_ReturnsThePartialLineOfEachStream()
    {
        var splitter = new LogLineSplitter();
        splitter.Append(Utf8($"{Stamp} out-tail"), isError: false);
        splitter.Append(Utf8($"{Stamp} err-tail"), isError: true);

        var lines = splitter.Flush();

        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, line => line is { Text: "out-tail", IsError: false });
        Assert.Contains(lines, line => line is { Text: "err-tail", IsError: true });
    }

    [Fact]
    public void Flush_IsEmptyWhenTheLastLineWasComplete()
    {
        var splitter = new LogLineSplitter();
        splitter.Append(Utf8($"{Stamp} done\n"), isError: false);

        Assert.Empty(splitter.Flush());
    }

    [Fact]
    public void Flush_DoesNotRepeatLines()
    {
        var splitter = new LogLineSplitter();
        splitter.Append(Utf8($"{Stamp} tail"), isError: false);

        Assert.Single(splitter.Flush());
        Assert.Empty(splitter.Flush());
    }

    [Fact]
    public void Flush_ReplacesAnIncompleteMultiByteCharacter()
    {
        var splitter = new LogLineSplitter();
        splitter.Append(Utf8($"{Stamp} broken ").Concat(new byte[] { 0xE2, 0x82 }).ToArray(), isError: false);

        var line = Assert.Single(splitter.Flush());

        Assert.Equal("broken �", line.Text);
    }

    [Fact]
    public void Append_EmitsAnOverlongLineInPiecesWithoutLosingText()
    {
        var splitter = new LogLineSplitter();
        const int chunkSize = 10_000;
        var text = new string('x', LogLineSplitter.MaxLineLength + 5_000);
        var lines = new List<LogLine>();

        for (var sent = 0; sent < text.Length; sent += chunkSize)
        {
            lines.AddRange(splitter.Append(Utf8(text.Substring(sent, Math.Min(chunkSize, text.Length - sent))), isError: false));
        }

        Assert.NotEmpty(lines);
        lines.AddRange(splitter.Flush());
        Assert.True(lines.Count >= 2);
        Assert.Equal(text, string.Concat(lines.Select(line => line.Text)));
    }
}
