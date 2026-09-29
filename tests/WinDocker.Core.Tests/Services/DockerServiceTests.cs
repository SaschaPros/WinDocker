using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using Docker.DotNet;
using Docker.DotNet.Models;
using WinDocker.Core.Models;
using WinDocker.Core.Services;
using WinDocker.Core.Tests.Support;

namespace WinDocker.Core.Tests.Services;

public class DockerServiceTests
{
    private static readonly DateTime Older = new(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Newer = new(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);

    private static DockerService CreateService(Func<System.Reflection.MethodInfo, object?[], object?> handler) =>
        new(() => FakeDockerClient.Create(handler));

    private static DockerService CreateFailingService(Exception exception) =>
        CreateService((_, _) => throw exception);

    [Fact]
    public async Task ListContainers_AsksForAllContainersAndReturnsTheNewestFirst()
    {
        ContainersListParameters? parameters = null;
        using var service = CreateService((method, args) =>
        {
            parameters = (ContainersListParameters)args[0]!;
            return (IList<ContainerListResponse>)
            [
                new ContainerListResponse { ID = "old", Names = ["/old"], Created = Older, Image = "i", State = "exited" },
                new ContainerListResponse { ID = "new", Names = ["/new"], Created = Newer, Image = "i", State = "running" },
            ];
        });

        var containers = await service.ListContainersAsync(all: true, TestContext.Current.CancellationToken);

        Assert.True(parameters!.All);
        Assert.Equal(["new", "old"], containers.Select(container => container.Id));
        Assert.Equal(["new", "old"], containers.Select(container => container.Name));
    }

    [Fact]
    public async Task ListContainers_CanExcludeStoppedContainers()
    {
        ContainersListParameters? parameters = null;
        using var service = CreateService((_, args) =>
        {
            parameters = (ContainersListParameters)args[0]!;
            return (IList<ContainerListResponse>)[];
        });

        var containers = await service.ListContainersAsync(all: false, TestContext.Current.CancellationToken);

        Assert.False(parameters!.All);
        Assert.Empty(containers);
    }

    [Fact]
    public async Task StartStopAndRemove_PassTheContainerIdAndForce()
    {
        var calls = new List<string>();
        using var service = CreateService((method, args) =>
        {
            switch (method.Name)
            {
                case "StartContainerAsync":
                    calls.Add($"start {args[0]}");
                    return true;
                case "StopContainerAsync":
                    calls.Add($"stop {args[0]}");
                    return true;
                case "RemoveContainerAsync":
                    calls.Add($"remove {args[0]} force={((ContainerRemoveParameters)args[1]!).Force}");
                    return null;
                default:
                    throw new NotSupportedException(method.Name);
            }
        });

        await service.StartContainerAsync("c1", TestContext.Current.CancellationToken);
        await service.StopContainerAsync("c2", TestContext.Current.CancellationToken);
        await service.RemoveContainerAsync("c3", force: true, TestContext.Current.CancellationToken);
        await service.RemoveContainerAsync("c4", force: false, TestContext.Current.CancellationToken);

        Assert.Equal(["start c1", "stop c2", "remove c3 force=True", "remove c4 force=False"], calls);
    }

    [Fact]
    public async Task ListImages_ReturnsOneRowPerTagNewestFirst()
    {
        using var service = CreateService((_, _) => (IList<ImagesListResponse>)
        [
            new ImagesListResponse { ID = "sha256:old", Created = Older, RepoTags = ["alpine:3"], Size = 8_000_000 },
            new ImagesListResponse { ID = "sha256:new", Created = Newer, RepoTags = ["nginx:latest", "nginx:1.27"], Size = 142_000_000 },
            new ImagesListResponse { ID = "sha256:dangling", Created = Newer, RepoTags = null!, Size = 1 },
        ]);

        var images = await service.ListImagesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            ["<none>:<none>", "nginx:1.27", "nginx:latest", "alpine:3"],
            images.Select(image => image.IsUntagged ? "<none>:<none>" : image.Reference));
    }

    [Fact]
    public async Task RemoveImage_PassesTheReference()
    {
        string? name = null;
        using var service = CreateService((_, args) =>
        {
            name = (string)args[0]!;
            return (IList<IDictionary<string, string>>)[];
        });

        await service.RemoveImageAsync("nginx:latest", TestContext.Current.CancellationToken);

        Assert.Equal("nginx:latest", name);
    }

    [Fact]
    public async Task ListVolumes_ReturnsThemSortedByName()
    {
        using var service = CreateService((_, _) => new VolumesListResponse
        {
            Volumes =
            [
                new VolumeResponse { Name = "logs", Driver = "local" },
                new VolumeResponse { Name = "Data", Driver = "local" },
            ],
        });

        var volumes = await service.ListVolumesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Data", "logs"], volumes.Select(volume => volume.Name));
    }

    [Fact]
    public async Task ListVolumes_IsEmptyWhenTheEngineAnswersNull()
    {
        using var service = CreateService((_, _) => new VolumesListResponse { Volumes = null! });

        Assert.Empty(await service.ListVolumesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveVolume_PassesTheNameWithoutForce()
    {
        string? name = null;
        bool? force = null;
        using var service = CreateService((_, args) =>
        {
            name = (string)args[0]!;
            force = (bool?)args[1];
            return null;
        });

        await service.RemoveVolumeAsync("data", TestContext.Current.CancellationToken);

        Assert.Equal("data", name);
        Assert.False(force);
    }

    public static TheoryData<Exception> ConnectivityFailures =>
    [
        new HttpRequestException("Connection failed.", new SocketException((int)SocketError.ConnectionRefused)),
        new IOException("pipe broken"),
        new EndOfStreamException(),
        new SocketException((int)SocketError.ConnectionRefused),
        new TimeoutException(),
        new UnauthorizedAccessException("Access to the path is denied."),
        new Win32Exception(5),
        new DockerConfigurationException("The Docker context 'x' does not exist."),
        new AggregateException(new SocketException((int)SocketError.ConnectionRefused), new SocketException((int)SocketError.ConnectionRefused)),
    ];

    [Theory]
    [MemberData(nameof(ConnectivityFailures))]
    public async Task Calls_TranslateConnectivityFailuresToDockerUnavailable(Exception failure)
    {
        using var service = CreateFailingService(failure);

        var exception = await Assert.ThrowsAsync<DockerUnavailableException>(
            () => service.ListContainersAsync(all: true, TestContext.Current.CancellationToken));

        Assert.Same(failure, exception.InnerException);
    }

    [Fact]
    public async Task Calls_TranslateARequestTimeoutToDockerUnavailable()
    {
        using var service = CreateFailingService(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."));

        await Assert.ThrowsAsync<DockerUnavailableException>(
            () => service.ListImagesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Calls_KeepACancellationRequestedByTheCaller()
    {
        using var service = CreateFailingService(new OperationCanceledException());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ListImagesAsync(cancellation.Token));
    }

    [Fact]
    public async Task Calls_LetApiErrorsPassThrough()
    {
        var failure = new DockerApiException(HttpStatusCode.Conflict, """{"message":"conflict"}""");
        using var service = CreateFailingService(failure);

        var exception = await Assert.ThrowsAsync<DockerApiException>(
            () => service.RemoveContainerAsync("c1", force: false, TestContext.Current.CancellationToken));

        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task Calls_LetAnAggregateOfOtherExceptionsPassThrough()
    {
        using var service = CreateFailingService(new AggregateException(new SocketException((int)SocketError.ConnectionRefused), new InvalidOperationException("bug")));

        await Assert.ThrowsAsync<AggregateException>(
            () => service.ListVolumesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Calls_LetOtherExceptionsPassThrough()
    {
        using var service = CreateFailingService(new InvalidOperationException("bug"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ListVolumesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Client_IsCreatedOnFirstUseOnly()
    {
        var created = 0;
        using var service = new DockerService(() =>
        {
            created++;
            return FakeDockerClient.Create((_, _) => (IList<ContainerListResponse>)[]);
        });
        Assert.Equal(0, created);

        await service.ListContainersAsync(true, TestContext.Current.CancellationToken);
        await service.ListContainersAsync(true, TestContext.Current.CancellationToken);

        Assert.Equal(1, created);
    }

    [Fact]
    public async Task Client_CreationFailureBecomesDockerUnavailableAndIsRetried()
    {
        var attempts = 0;
        using var service = new DockerService(() =>
        {
            attempts++;
            return attempts == 1
                ? throw new DockerConfigurationException("The Docker host 'x' is invalid.")
                : FakeDockerClient.Create((_, _) => (IList<ContainerListResponse>)[]);
        });

        var exception = await Assert.ThrowsAsync<DockerUnavailableException>(
            () => service.ListContainersAsync(true, TestContext.Current.CancellationToken));
        Assert.IsType<DockerConfigurationException>(exception.InnerException);

        Assert.Empty(await service.ListContainersAsync(true, TestContext.Current.CancellationToken));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Dispose_DisposesTheClientAndRejectsFurtherCalls()
    {
        IDockerClient? client = null;
        var service = new DockerService(() => client = FakeDockerClient.Create((_, _) => (IList<ContainerListResponse>)[]));
        await service.ListContainersAsync(true, TestContext.Current.CancellationToken);

        service.Dispose();

        Assert.True(((FakeDockerClient)client!).Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => service.ListContainersAsync(true, TestContext.Current.CancellationToken));
    }
}
