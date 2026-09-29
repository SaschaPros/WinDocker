using System.Net;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using WinDocker.Core.Models;
using WinDocker.Core.Services;
using WinDocker.Core.Tests.Support;
using static WinDocker.Core.Tests.Support.TestStreams;

namespace WinDocker.Core.Tests.Services;

public class DockerServiceLogsTests
{
    private const string Stamp = "2026-09-29T10:15:30.123456789Z";

    private static readonly DateTimeOffset StampInstant = new DateTimeOffset(2026, 9, 29, 10, 15, 30, TimeSpan.Zero).AddTicks(1_234_567);

    private static DockerService CreateService(Func<ContainerLogsParameters, MultiplexedStream> logs) =>
        new(() => FakeDockerClient.Create((method, args) =>
            method.Name == "GetContainerLogsAsync" ? logs((ContainerLogsParameters)args[1]!) : throw new NotSupportedException(method.Name)));

    private static DockerService CreateService(MultiplexedStream stream) => CreateService(_ => stream);

    private static async Task<List<LogLine>> ReadAllAsync(DockerService service, int tail = 100, bool follow = false)
    {
        var lines = new List<LogLine>();
        await foreach (var line in service.StreamLogsAsync("c1", tail, follow, TestContext.Current.CancellationToken))
        {
            lines.Add(line);
        }

        return lines;
    }

    [Fact]
    public async Task StreamLogs_ReadsStdoutAndStderrInOrder()
    {
        var bytes = Frames(
            (Stdout, $"{Stamp} out-1\n"),
            (Stderr, $"{Stamp} err-1\n"),
            (Stdout, $"{Stamp} out-2\n"));
        using var service = CreateService(new MultiplexedStream(new MemoryStream(bytes), multiplexed: true));

        var lines = await ReadAllAsync(service);

        Assert.Equal([("out-1", false), ("err-1", true), ("out-2", false)], lines.Select(line => (line.Text, line.IsError)));
        Assert.All(lines, line => Assert.Equal(StampInstant, line.Timestamp));
    }

    [Fact]
    public async Task StreamLogs_ReadsTheRawStreamOfATtyContainer()
    {
        var bytes = Encoding.UTF8.GetBytes($"{Stamp} tty-out\r\n{Stamp} tty-err\r\n");
        using var service = CreateService(new MultiplexedStream(new MemoryStream(bytes), multiplexed: false));

        var lines = await ReadAllAsync(service);

        Assert.Equal([("tty-out", false), ("tty-err", false)], lines.Select(line => (line.Text, line.IsError)));
    }

    [Fact]
    public async Task StreamLogs_ReassemblesALineThatIsLargerThanTheReadBuffer()
    {
        // The euro sign has three bytes, so a 16 KiB read ends in the middle of a character.
        var text = string.Concat(Enumerable.Repeat("€", 10_000));
        var bytes = Frames((Stdout, $"{Stamp} {text}\n{Stamp} after\n"));
        using var service = CreateService(new MultiplexedStream(new MemoryStream(bytes), multiplexed: true));

        var lines = await ReadAllAsync(service);

        Assert.Equal([text, "after"], lines.Select(line => line.Text));
    }

    [Fact]
    public async Task StreamLogs_ReturnsTheLastPartialLineAtTheEndOfTheStream()
    {
        var bytes = Frames((Stdout, $"{Stamp} complete\n"), (Stdout, $"{Stamp} trailing"));
        using var service = CreateService(new MultiplexedStream(new MemoryStream(bytes), multiplexed: true));

        var lines = await ReadAllAsync(service);

        Assert.Equal(["complete", "trailing"], lines.Select(line => line.Text));
    }

    [Theory]
    [InlineData(1000, false, "1000", false)]
    [InlineData(5, true, "5", true)]
    [InlineData(0, false, "all", false)]
    [InlineData(-1, true, "all", true)]
    public async Task StreamLogs_RequestsTimestampsStdoutStderrTailAndFollow(int tail, bool follow, string expectedTail, bool expectedFollow)
    {
        ContainerLogsParameters? requested = null;
        using var service = CreateService(parameters =>
        {
            requested = parameters;
            return new MultiplexedStream(new MemoryStream(), multiplexed: true);
        });

        await ReadAllAsync(service, tail, follow);

        Assert.True(requested!.ShowStdout);
        Assert.True(requested.ShowStderr);
        Assert.True(requested.Timestamps);
        Assert.Equal(expectedTail, requested.Tail);
        Assert.Equal(expectedFollow, requested.Follow);
    }

    [Fact]
    public async Task StreamLogs_LetsApiErrorsPassThrough()
    {
        var failure = new DockerApiException(HttpStatusCode.NotFound, """{"message":"No such container: c1"}""");
        using var service = CreateService(_ => throw failure);

        var exception = await Assert.ThrowsAsync<DockerApiException>(() => ReadAllAsync(service));

        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task StreamLogs_TranslatesAConnectionFailureToDockerUnavailable()
    {
        using var service = CreateService(_ => throw new HttpRequestException("Connection failed."));

        await Assert.ThrowsAsync<DockerUnavailableException>(() => ReadAllAsync(service));
    }

    [Fact]
    public async Task StreamLogs_ReturnsTheLinesBeforeABrokenConnectionAndThenFails()
    {
        var bytes = Frames((Stdout, $"{Stamp} before\n"));
        using var service = CreateService(new MultiplexedStream(new FailingStream(bytes, new IOException("pipe broken")), multiplexed: true));
        var lines = new List<LogLine>();

        await Assert.ThrowsAsync<DockerUnavailableException>(async () =>
        {
            await foreach (var line in service.StreamLogsAsync("c1", 100, follow: true, TestContext.Current.CancellationToken))
            {
                lines.Add(line);
            }
        });

        Assert.Equal(["before"], lines.Select(line => line.Text));
    }

    [Fact]
    public async Task StreamLogs_TranslatesATruncatedFrameToDockerUnavailable()
    {
        var bytes = Frames((Stdout, $"{Stamp} cut off\n"))[..12];
        using var service = CreateService(new MultiplexedStream(new MemoryStream(bytes), multiplexed: true));

        await Assert.ThrowsAsync<DockerUnavailableException>(() => ReadAllAsync(service));
    }

    [Fact]
    public async Task StreamLogs_DisposesTheStreamAtTheEnd()
    {
        var stream = new TrackedMemoryStream(Frames((Stdout, $"{Stamp} one\n")));
        using var service = CreateService(new MultiplexedStream(stream, multiplexed: true));

        await ReadAllAsync(service);

        Assert.True(stream.WasDisposed);
    }

    [Fact]
    public async Task StreamLogs_DisposesTheStreamWhenTheConsumerStopsEarly()
    {
        var stream = new TrackedMemoryStream(Frames((Stdout, $"{Stamp} one\n{Stamp} two\n")));
        using var service = CreateService(new MultiplexedStream(stream, multiplexed: true));

        await foreach (var _ in service.StreamLogsAsync("c1", 100, follow: false, TestContext.Current.CancellationToken))
        {
            break;
        }

        Assert.True(stream.WasDisposed);
    }

    [Fact]
    public async Task StreamLogs_CancellationDisposesTheStreamSoABlockedReadReturns()
    {
        var stream = new BlockingStream();
        using var service = CreateService(new MultiplexedStream(stream, multiplexed: true));
        using var cancellation = new CancellationTokenSource();

        var reading = Task.Run(
            async () =>
            {
                await foreach (var _ in service.StreamLogsAsync("c1", 100, follow: true, cancellation.Token))
                {
                }
            },
            TestContext.Current.CancellationToken);
        await stream.Blocked.Within();
        Assert.False(stream.IsDisposed);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading.Within());
        Assert.True(stream.IsDisposed);
    }

    [Fact]
    public async Task StreamLogs_ACancelledTokenStopsBeforeTheFirstLine()
    {
        using var service = CreateService(new MultiplexedStream(new BlockingStream(), multiplexed: true));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in service.StreamLogsAsync("c1", 100, follow: true, cancellation.Token))
            {
            }
        });
    }
}
