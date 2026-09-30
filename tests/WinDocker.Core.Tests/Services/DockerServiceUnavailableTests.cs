using System.Net;
using System.Net.Sockets;
using Docker.DotNet;
using Docker.DotNet.NPipe;
using WinDocker.Core.Services;
using WinDocker.Core.Tests.Support;

namespace WinDocker.Core.Tests.Services;

/// <summary>Runs the real client against endpoints where nothing listens or nothing answers, to see what the library throws.</summary>
public class DockerServiceUnavailableTests
{
    private static DockerService ServiceFor(string endpoint) =>
        new(() => new DockerClientBuilder().WithEndpoint(new Uri(endpoint)).Build());

    [Fact]
    public async Task ClosedTcpPort_IsUnavailable()
    {
        using var service = ServiceFor("tcp://127.0.0.1:1");

        var exception = await Assert.ThrowsAsync<DockerUnavailableException>(
            () => service.ListContainersAsync(all: true, TestContext.Current.CancellationToken));

        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public async Task ClosedTcpPort_MakesALogStreamUnavailable()
    {
        using var service = ServiceFor("tcp://127.0.0.1:1");

        await Assert.ThrowsAsync<DockerUnavailableException>(async () =>
        {
            await foreach (var _ in service.StreamLogsAsync("c1", 100, follow: true, TestContext.Current.CancellationToken))
            {
            }
        });
    }

    [Fact]
    public async Task MissingUnixSocket_IsUnavailable()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Docker uses a named pipe on Windows.");
        var socket = Path.Combine(Path.GetTempPath(), $"windocker-missing-{Guid.NewGuid():N}.sock");
        using var service = ServiceFor($"unix://{socket}");

        var exception = await Assert.ThrowsAsync<DockerUnavailableException>(
            () => service.ListImagesAsync(TestContext.Current.CancellationToken));

        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public async Task MissingNamedPipe_IsUnavailable()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Named pipes exist on Windows only.");
        using var service = new DockerService(() => new DockerClientBuilder()
            .WithEndpoint(new Uri($"npipe://./pipe/windocker_missing_{Guid.NewGuid():N}"))
            .WithTransportOptions(new NPipeTransportOptions { ConnectTimeout = TimeSpan.FromSeconds(1) })
            .Build());

        var exception = await Assert.ThrowsAsync<DockerUnavailableException>(
            () => service.ListVolumesAsync(TestContext.Current.CancellationToken));

        Assert.IsType<TimeoutException>(exception.InnerException);
    }

    [Fact]
    public async Task UnsupportedEndpoint_IsUnavailable()
    {
        using var service = ServiceFor("ssh://user@example.com");

        var exception = await Assert.ThrowsAsync<DockerUnavailableException>(
            () => service.ListVolumesAsync(TestContext.Current.CancellationToken));

        Assert.IsType<SshDockerEndpointNotSupportedException>(exception.InnerException);
    }

    [Fact]
    public async Task UnresponsiveTcpEndpoint_IsUnavailableOnceTheClientTimeoutElapses()
    {
        // Nothing ever accepts on this listener, but the kernel completes the handshake from the backlog. The client
        // connects, sends its request and then waits for an answer that never comes, so only its own timeout ends the call.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var service = new DockerService(() => new DockerClientBuilder()
            .WithEndpoint(new Uri($"tcp://127.0.0.1:{port}"))
            .WithTimeout(TimeSpan.FromMilliseconds(200))
            .Build());
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var exception = await Assert.ThrowsAsync<DockerUnavailableException>(
            () => service.ListContainersAsync(all: true, TestContext.Current.CancellationToken).Within());

        Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
        Assert.InRange(clock.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ClientsFromTheFactory_ReportAMissingUnixSocketAsUnavailable()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Docker uses a named pipe on Windows.");
        var socket = Path.Combine(Path.GetTempPath(), $"windocker-missing-{Guid.NewGuid():N}.sock");
        using var service = new DockerService(() => DockerClientFactory.Create(new Uri($"unix://{socket}")));

        await Assert.ThrowsAsync<DockerUnavailableException>(
            () => service.ListContainersAsync(all: true, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ClientsFromTheFactory_ReportAClosedTcpPortAsUnavailable()
    {
        using var service = new DockerService(() => DockerClientFactory.Create(new Uri("tcp://127.0.0.1:1")));

        await Assert.ThrowsAsync<DockerUnavailableException>(
            () => service.ListContainersAsync(all: true, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ClientsFromTheFactory_GiveUpOnAMissingNamedPipeSoonerThanTheLibraryDefault()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Named pipes exist on Windows only.");
        using var service = new DockerService(() => DockerClientFactory.Create(new Uri($"npipe://./pipe/windocker_missing_{Guid.NewGuid():N}")));
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var exception = await Assert.ThrowsAsync<DockerUnavailableException>(
            () => service.ListContainersAsync(all: true, TestContext.Current.CancellationToken));

        Assert.IsType<TimeoutException>(exception.InnerException);
        Assert.InRange(clock.Elapsed, TimeSpan.Zero, DockerClientFactory.NamedPipeConnectTimeout + TimeSpan.FromSeconds(4));
    }

    [Theory]
    [InlineData("npipe://./pipe/docker_engine")]
    [InlineData("unix:///var/run/docker.sock")]
    [InlineData("tcp://127.0.0.1:2375")]
    [InlineData("http://127.0.0.1:2375")]
    public void Factory_BuildsAClientForEverySupportedEndpoint(string endpoint)
    {
        using var client = DockerClientFactory.Create(new Uri(endpoint));

        Assert.Equal(new Uri(endpoint), client.Options.Endpoint);
    }

    [Fact]
    public void Factory_RejectsSshEndpoints() =>
        Assert.Throws<SshDockerEndpointNotSupportedException>(() => DockerClientFactory.Create(new Uri("ssh://user@example.com")));
}
