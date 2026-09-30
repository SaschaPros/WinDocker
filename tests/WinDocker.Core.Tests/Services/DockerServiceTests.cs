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

    /// <summary>The <c>name=value</c> pairs of a filter parameter, sorted; empty when the request has no filter.</summary>
    private static string[] Flatten(IDictionary<string, IDictionary<string, bool>>? filters) =>
        filters is null
            ? []
            : [.. filters
                .SelectMany(filter => filter.Value.Where(value => value.Value).Select(value => $"{filter.Key}={value.Key}"))
                .Order(StringComparer.Ordinal)];

    private static Dictionary<string, string> ProjectLabels(string project) =>
        new() { ["com.docker.compose.project"] = project, ["com.docker.compose.network"] = "default" };

    private static ContainerListResponse ComposeContainer(string id, string project, string service, string state, DateTime created) =>
        new()
        {
            ID = id,
            Names = [$"/{project}-{service}-1"],
            Image = "i",
            Created = created,
            State = state,
            Labels = new Dictionary<string, string>
            {
                ["com.docker.compose.project"] = project,
                ["com.docker.compose.service"] = service,
                ["com.docker.compose.oneoff"] = "False",
            },
        };

    [Fact]
    public async Task RestartContainer_PassesTheContainerId()
    {
        var calls = new List<string>();
        using var service = CreateService((method, args) =>
        {
            calls.Add($"{method.Name} {args[0]} {args[1]!.GetType().Name}");
            return null;
        });

        await service.RestartContainerAsync("c1", TestContext.Current.CancellationToken);

        Assert.Equal(["RestartContainerAsync c1 ContainerRestartParameters"], calls);
    }

    [Fact]
    public async Task PruneContainers_SendsNoFilterAndCountsTheRemovedContainers()
    {
        ContainersPruneParameters? parameters = null;
        using var service = CreateService((method, args) =>
        {
            Assert.Equal("PruneContainersAsync", method.Name);
            parameters = (ContainersPruneParameters)args[0]!;
            return new ContainersPruneResponse { ContainersDeleted = ["a", "b", "c"], SpaceReclaimed = 12_300_000 };
        });

        var result = await service.PruneContainersAsync(TestContext.Current.CancellationToken);

        Assert.Null(parameters!.Filters);
        Assert.Equal(new PruneResult(3, 12_300_000), result);
    }

    [Fact]
    public async Task Prune_IsEmptyWhenTheEngineAnswersNullLists()
    {
        using var service = CreateService((method, _) => method.Name switch
        {
            "PruneContainersAsync" => new ContainersPruneResponse { ContainersDeleted = null!, SpaceReclaimed = 0 },
            "PruneImagesAsync" => new ImagesPruneResponse { ImagesDeleted = null!, SpaceReclaimed = 0 },
            "PruneAsync" => new VolumesPruneResponse { VolumesDeleted = null!, SpaceReclaimed = 0 },
            _ => throw new NotSupportedException(method.Name),
        });
        var token = TestContext.Current.CancellationToken;

        Assert.Equal(new PruneResult(0, 0), await service.PruneContainersAsync(token));
        Assert.Equal(new PruneResult(0, 0), await service.PruneImagesAsync(allUnused: true, token));
        Assert.Equal(new PruneResult(0, 0), await service.PruneVolumesAsync(includeNamed: true, token));
    }

    [Fact]
    public async Task Prune_ClampsAnAbsurdlyLargeReclaimedSpace()
    {
        using var service = CreateService((_, _) => new ContainersPruneResponse { ContainersDeleted = ["a"], SpaceReclaimed = ulong.MaxValue });

        var result = await service.PruneContainersAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new PruneResult(1, long.MaxValue), result);
    }

    [Theory]
    [InlineData(false, "dangling=true")]
    [InlineData(true, "dangling=false")]
    public async Task PruneImages_AsksForDanglingImagesOrForAllUnusedOnes(bool allUnused, string expectedFilter)
    {
        ImagesPruneParameters? parameters = null;
        using var service = CreateService((method, args) =>
        {
            Assert.Equal("PruneImagesAsync", method.Name);
            parameters = (ImagesPruneParameters)args[0]!;
            return new ImagesPruneResponse { ImagesDeleted = [], SpaceReclaimed = 0 };
        });

        await service.PruneImagesAsync(allUnused, TestContext.Current.CancellationToken);

        Assert.Equal([expectedFilter], Flatten(parameters!.Filters));
    }

    [Fact]
    public async Task PruneImages_CountsTheDeletedImagesAndNotTheUntaggedOnes()
    {
        using var service = CreateService((_, _) => new ImagesPruneResponse
        {
            ImagesDeleted =
            [
                new ImageDeleteResponse { Untagged = "nginx:latest" },
                new ImageDeleteResponse { Untagged = "nginx@sha256:aaa" },
                new ImageDeleteResponse { Deleted = "sha256:bbb" },
                new ImageDeleteResponse { Deleted = "sha256:ccc" },
                new ImageDeleteResponse { Untagged = "redis:7", Deleted = string.Empty },
            ],
            SpaceReclaimed = 142_000_000,
        });

        var result = await service.PruneImagesAsync(allUnused: true, TestContext.Current.CancellationToken);

        Assert.Equal(new PruneResult(2, 142_000_000), result);
    }

    [Theory]
    [InlineData(false, new string[0])]
    [InlineData(true, new[] { "all=true" })]
    public async Task PruneVolumes_ExtendsThePruneToNamedVolumesOnlyWhenAsked(bool includeNamed, string[] expectedFilters)
    {
        VolumesPruneParameters? parameters = null;
        using var service = CreateService((method, args) =>
        {
            Assert.Equal("PruneAsync", method.Name);
            parameters = (VolumesPruneParameters)args[0]!;
            return new VolumesPruneResponse { VolumesDeleted = ["data", "logs"], SpaceReclaimed = 4096 };
        });

        var result = await service.PruneVolumesAsync(includeNamed, TestContext.Current.CancellationToken);

        Assert.Equal(expectedFilters, Flatten(parameters!.Filters));
        Assert.Equal(new PruneResult(2, 4096), result);
    }

    [Fact]
    public async Task Prune_WithALabelOnlyTouchesWhatCarriesTheLabel()
    {
        ContainersPruneParameters? containers = null;
        ImagesPruneParameters? images = null;
        VolumesPruneParameters? volumes = null;
        using var service = CreateService((method, args) =>
        {
            switch (method.Name)
            {
                case "PruneContainersAsync":
                    containers = (ContainersPruneParameters)args[0]!;
                    return new ContainersPruneResponse { ContainersDeleted = [], SpaceReclaimed = 0 };
                case "PruneImagesAsync":
                    images = (ImagesPruneParameters)args[0]!;
                    return new ImagesPruneResponse { ImagesDeleted = [], SpaceReclaimed = 0 };
                case "PruneAsync":
                    volumes = (VolumesPruneParameters)args[0]!;
                    return new VolumesPruneResponse { VolumesDeleted = [], SpaceReclaimed = 0 };
                default:
                    throw new NotSupportedException(method.Name);
            }
        });
        var token = TestContext.Current.CancellationToken;

        await service.PruneContainersAsync("windocker-tests=x", token);
        await service.PruneImagesAsync(allUnused: false, "windocker-tests=x", token);
        await service.PruneVolumesAsync(includeNamed: true, "windocker-tests=x", token);

        Assert.Equal(["label=windocker-tests=x"], Flatten(containers!.Filters));
        Assert.Equal(["dangling=true", "label=windocker-tests=x"], Flatten(images!.Filters));
        Assert.Equal(["all=true", "label=windocker-tests=x"], Flatten(volumes!.Filters));

        await service.PruneVolumesAsync(includeNamed: false, "windocker-tests=x", token);

        Assert.Equal(["label=windocker-tests=x"], Flatten(volumes.Filters));
    }

    [Fact]
    public async Task ListComposeProjects_AsksForAllContainersWithAProjectLabelAndGroupsThem()
    {
        ContainersListParameters? parameters = null;
        using var service = CreateService((_, args) =>
        {
            parameters = (ContainersListParameters)args[0]!;
            return (IList<ContainerListResponse>)
            [
                ComposeContainer("c1", "shop", "web", "running", Newer),
                ComposeContainer("c2", "shop", "db", "exited", Older),
                ComposeContainer("c3", "blog", "app", "running", Newer),
            ];
        });

        var projects = await service.ListComposeProjectsAsync(TestContext.Current.CancellationToken);

        Assert.True(parameters!.All);
        Assert.Equal(["label=com.docker.compose.project"], Flatten(parameters.Filters));
        Assert.Equal(["blog", "shop"], projects.Select(project => project.Name));
        var shop = projects[1];
        Assert.Equal(["db", "web"], shop.Services);
        Assert.Equal(1, shop.RunningCount);
        Assert.Equal(2, shop.TotalCount);
        Assert.Equal(["c2", "c1"], shop.Containers.Select(container => container.Id));
        Assert.Equal(new DateTimeOffset(Older), shop.CreatedAt);
    }

    [Fact]
    public async Task ListComposeProjects_IgnoresContainersWithoutAProjectEvenWhenTheEngineIgnoresTheFilter()
    {
        using var service = CreateService((_, _) => (IList<ContainerListResponse>)
        [
            new ContainerListResponse { ID = "plain", Names = ["/plain"], Image = "i", Created = Newer, State = "running", Labels = null! },
            ComposeContainer("c1", "shop", "web", "running", Newer),
        ]);

        var projects = await service.ListComposeProjectsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["shop"], projects.Select(project => project.Name));
        Assert.Equal(1, projects[0].TotalCount);
    }

    [Fact]
    public async Task ListComposeProjects_IsEmptyWithoutContainers()
    {
        using var service = CreateService((_, _) => (IList<ContainerListResponse>)[]);

        Assert.Empty(await service.ListComposeProjectsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveComposeNetworks_DeletesTheNetworksOfTheProjectOneByOne()
    {
        NetworksListParameters? parameters = null;
        var deleted = new List<string>();
        using var service = CreateService((method, args) =>
        {
            switch (method.Name)
            {
                case "ListNetworksAsync":
                    parameters = (NetworksListParameters)args[0]!;
                    return (IList<NetworkResponse>)
                    [
                        new NetworkResponse { ID = "n1", Name = "shop_default", Labels = ProjectLabels("shop") },
                        new NetworkResponse { ID = "n2", Name = "shop_backend", Labels = ProjectLabels("shop") },
                    ];
                case "DeleteNetworkAsync":
                    deleted.Add((string)args[0]!);
                    return null;
                default:
                    throw new NotSupportedException(method.Name);
            }
        });

        await service.RemoveComposeNetworksAsync("shop", TestContext.Current.CancellationToken);

        Assert.Equal(["label=com.docker.compose.project=shop"], Flatten(parameters!.Filters));
        Assert.Equal(["n1", "n2"], deleted);
    }

    [Fact]
    public async Task RemoveComposeNetworks_LeavesTheNetworksOfOtherProjectsAloneWhenTheEngineIgnoresTheFilter()
    {
        var deleted = new List<string>();
        using var service = CreateService((method, args) =>
        {
            if (method.Name == "DeleteNetworkAsync")
            {
                deleted.Add((string)args[0]!);
                return null;
            }

            return (IList<NetworkResponse>)
            [
                new NetworkResponse { ID = "n1", Name = "shop_default", Labels = ProjectLabels("shop") },
                new NetworkResponse { ID = "n2", Name = "blog_default", Labels = ProjectLabels("blog") },
                new NetworkResponse { ID = "n3", Name = "bridge", Labels = null! },
                new NetworkResponse { ID = "n4", Name = "custom", Labels = new Dictionary<string, string> { ["team"] = "shop" } },
            ];
        });

        await service.RemoveComposeNetworksAsync("shop", TestContext.Current.CancellationToken);

        Assert.Equal(["n1"], deleted);
    }

    [Fact]
    public async Task RemoveComposeNetworks_DoesNothingWhenTheProjectHasNoNetworks()
    {
        var calls = new List<string>();
        using var service = CreateService((method, _) =>
        {
            calls.Add(method.Name);
            return (IList<NetworkResponse>)[];
        });

        await service.RemoveComposeNetworksAsync("shop", TestContext.Current.CancellationToken);

        Assert.Equal(["ListNetworksAsync"], calls);
    }

    [Fact]
    public async Task RemoveComposeNetworks_StopsAtTheFirstFailureAndLetsTheEnginesErrorPass()
    {
        var failure = new DockerApiException(HttpStatusCode.Forbidden, """{"message":"network has active endpoints"}""");
        var deleted = new List<string>();
        using var service = CreateService((method, args) =>
        {
            if (method.Name == "DeleteNetworkAsync")
            {
                deleted.Add((string)args[0]!);
                throw failure;
            }

            return (IList<NetworkResponse>)
            [
                new NetworkResponse { ID = "n1", Labels = ProjectLabels("shop") },
                new NetworkResponse { ID = "n2", Labels = ProjectLabels("shop") },
            ];
        });

        var exception = await Assert.ThrowsAsync<DockerApiException>(
            () => service.RemoveComposeNetworksAsync("shop", TestContext.Current.CancellationToken));

        Assert.Same(failure, exception);
        Assert.Equal(["n1"], deleted);
    }

    [Fact]
    public async Task RemoveComposeVolumes_RemovesTheVolumesOfTheProjectWithoutForce()
    {
        VolumesListParameters? parameters = null;
        var removed = new List<string>();
        using var service = CreateService((method, args) =>
        {
            switch (method.Name)
            {
                case "ListAsync":
                    parameters = (VolumesListParameters)args[0]!;
                    return new VolumesListResponse
                    {
                        Volumes =
                        [
                            new VolumeResponse { Name = "shop_data", Labels = ProjectLabels("shop") },
                            new VolumeResponse { Name = "shop_logs", Labels = ProjectLabels("shop") },
                        ],
                    };
                case "RemoveAsync":
                    removed.Add($"{args[0]} force={args[1]}");
                    return null;
                default:
                    throw new NotSupportedException(method.Name);
            }
        });

        await service.RemoveComposeVolumesAsync("shop", TestContext.Current.CancellationToken);

        Assert.Equal(["label=com.docker.compose.project=shop"], Flatten(parameters!.Filters));
        Assert.Equal(["shop_data force=False", "shop_logs force=False"], removed);
    }

    [Fact]
    public async Task RemoveComposeVolumes_LeavesTheVolumesOfOtherProjectsAloneWhenTheEngineIgnoresTheFilter()
    {
        var removed = new List<string>();
        using var service = CreateService((method, args) =>
        {
            if (method.Name == "RemoveAsync")
            {
                removed.Add((string)args[0]!);
                return null;
            }

            return new VolumesListResponse
            {
                Volumes =
                [
                    new VolumeResponse { Name = "shop_data", Labels = ProjectLabels("shop") },
                    new VolumeResponse { Name = "blog_data", Labels = ProjectLabels("blog") },
                    new VolumeResponse { Name = "plain", Labels = null! },
                ],
            };
        });

        await service.RemoveComposeVolumesAsync("shop", TestContext.Current.CancellationToken);

        Assert.Equal(["shop_data"], removed);
    }

    [Fact]
    public async Task RemoveComposeVolumes_DoesNothingWhenTheEngineAnswersNull()
    {
        var calls = new List<string>();
        using var service = CreateService((method, _) =>
        {
            calls.Add(method.Name);
            return new VolumesListResponse { Volumes = null! };
        });

        await service.RemoveComposeVolumesAsync("shop", TestContext.Current.CancellationToken);

        Assert.Equal(["ListAsync"], calls);
    }

    [Fact]
    public async Task RemoveComposeVolumes_StopsAtTheFirstFailureAndLetsTheEnginesErrorPass()
    {
        var failure = new DockerApiException(HttpStatusCode.Conflict, """{"message":"volume is in use"}""");
        var removed = new List<string>();
        using var service = CreateService((method, args) =>
        {
            if (method.Name == "RemoveAsync")
            {
                removed.Add((string)args[0]!);
                throw failure;
            }

            return new VolumesListResponse
            {
                Volumes =
                [
                    new VolumeResponse { Name = "shop_data", Labels = ProjectLabels("shop") },
                    new VolumeResponse { Name = "shop_logs", Labels = ProjectLabels("shop") },
                ],
            };
        });

        var exception = await Assert.ThrowsAsync<DockerApiException>(
            () => service.RemoveComposeVolumesAsync("shop", TestContext.Current.CancellationToken));

        Assert.Same(failure, exception);
        Assert.Equal(["shop_data"], removed);
    }

    private static readonly (string Name, Func<DockerService, Task> Call)[] NewOperations =
    [
        ("RestartContainer", service => service.RestartContainerAsync("c1")),
        ("PruneContainers", service => service.PruneContainersAsync()),
        ("PruneImages", service => service.PruneImagesAsync(allUnused: true)),
        ("PruneVolumes", service => service.PruneVolumesAsync(includeNamed: true)),
        ("ListComposeProjects", service => service.ListComposeProjectsAsync()),
        ("RemoveComposeNetworks", service => service.RemoveComposeNetworksAsync("shop")),
        ("RemoveComposeVolumes", service => service.RemoveComposeVolumesAsync("shop")),
    ];

    [Fact]
    public async Task PruneRestartAndComposeCalls_TranslateConnectivityFailuresAndLetApiErrorsPass()
    {
        var apiError = new DockerApiException(HttpStatusCode.Conflict, """{"message":"conflict"}""");

        foreach (var (name, call) in NewOperations)
        {
            using var unreachable = CreateFailingService(new IOException("pipe broken"));
            var unavailable = await Record.ExceptionAsync(() => call(unreachable));
            Assert.True(unavailable is DockerUnavailableException, $"{name} threw {unavailable?.GetType().Name ?? "nothing"}.");

            using var rejecting = CreateFailingService(apiError);
            var rejected = await Record.ExceptionAsync(() => call(rejecting));
            Assert.True(ReferenceEquals(apiError, rejected), $"{name} threw {rejected?.GetType().Name ?? "nothing"}.");
        }
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
