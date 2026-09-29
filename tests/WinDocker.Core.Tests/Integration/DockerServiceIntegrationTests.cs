using System.Diagnostics;
using Docker.DotNet;
using Docker.DotNet.Models;
using WinDocker.Core.Docker;
using WinDocker.Core.Models;
using WinDocker.Core.Services;
using WinDocker.Core.Tests.Support;
using WinDocker.Core.ViewModels;

namespace WinDocker.Core.Tests.Integration;

/// <summary>
/// Runs <see cref="DockerService"/> against the local Docker engine (DOCKER_HOST, docker context or the default
/// pipe or socket). Skipped when no engine answers. Tests that start Linux containers are skipped on Windows engines.
/// </summary>
public class DockerServiceIntegrationTests(DockerEngineFixture engine) : IClassFixture<DockerEngineFixture>
{
    /// <summary>Prints out-N, waits a second, prints err-N to stderr, waits a second; repeated N times.</summary>
    private static string[] Chatty(int rounds) =>
        ["sh", "-c", $"for i in $(seq 1 {rounds}); do echo \"out-$i\"; sleep 1; echo \"err-$i\" >&2; sleep 1; done"];

    /// <summary>Stays quiet and exits promptly on SIGTERM (a shell as PID 1 ignores it otherwise).</summary>
    private static readonly string[] Quiet = ["sh", "-c", "trap 'exit 0' TERM; while true; do sleep 1; done"];

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private static CancellationTokenSource TimeoutAfter(TimeSpan timeout)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(TestToken);
        source.CancelAfter(timeout);
        return source;
    }

    /// <summary>The engine may run in a VM whose clock is off by minutes, so this only rules out wrong parsing.</summary>
    private static void AssertRecent(DateTimeOffset timestamp) =>
        Assert.InRange(timestamp, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

    /// <summary>Polls <paramref name="condition"/> every 100 ms until it holds; fails the test when it still does not after <paramref name="timeout"/>.</summary>
    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string description, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (!await condition())
        {
            if (clock.Elapsed >= timeout)
            {
                Assert.Fail($"Gave up after {timeout.TotalSeconds:0.#} s waiting for {description}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), TestToken);
        }
    }

    private IDockerClient RequireEngine()
    {
        Assert.SkipUnless(engine.Client is not null, engine.UnavailableReason ?? "No Docker engine.");
        return engine.Client!;
    }

    private async Task<(IDockerClient Client, string Image)> RequireLinuxEngineAsync()
    {
        var client = RequireEngine();
        Assert.SkipUnless(engine.IsLinuxEngine, "The engine runs Windows containers.");
        using var timeout = TimeoutAfter(TimeSpan.FromMinutes(3));
        var image = await engine.EnsureImageAsync(timeout.Token);
        Assert.SkipWhen(image is null, "No test image could be pulled.");
        return (client, image!);
    }

    private static async Task<string> CreateContainerAsync(IDockerClient client, string image, string[] command, bool tty = false, bool start = true)
    {
        var created = await client.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Image = image,
                Cmd = command,
                Tty = tty,
                Name = $"windocker-tests-{Guid.NewGuid():N}",
                Labels = new Dictionary<string, string> { [DockerEngineFixture.TestLabel] = "true" },
                HostConfig = new HostConfig { NetworkMode = "none" },
            },
            TestToken);

        if (start)
        {
            await client.Containers.StartContainerAsync(created.ID, null, TestToken);
        }

        return created.ID;
    }

    private static async Task RemoveQuietlyAsync(IDockerClient client, string containerId)
    {
        try
        {
            await client.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters { Force = true }, CancellationToken.None);
        }
        catch (Exception)
        {
            // Best effort; the fixture removes leftovers too.
        }
    }

    private static async Task<List<LogLine>> ReadAsync(DockerService service, string id, int tail, bool follow, CancellationToken cancellationToken)
    {
        var lines = new List<LogLine>();
        await foreach (var line in service.StreamLogsAsync(id, tail, follow, cancellationToken))
        {
            lines.Add(line);
        }

        return lines;
    }

    [Fact]
    public async Task Volumes_CanBeCreatedListedAndRemoved()
    {
        var client = RequireEngine();
        using var service = new DockerService();
        var name = $"windocker-tests-{Guid.NewGuid():N}";
        await client.Volumes.CreateAsync(new VolumesCreateParameters { Name = name, Labels = new Dictionary<string, string> { [DockerEngineFixture.TestLabel] = "true" } }, TestToken);
        try
        {
            var volumes = await service.ListVolumesAsync(TestToken);

            var volume = Assert.Single(volumes, candidate => candidate.Name == name);
            Assert.Equal("local", volume.Driver);
            Assert.False(string.IsNullOrEmpty(volume.Mountpoint));
            Assert.NotNull(volume.CreatedAt);
            AssertRecent(volume.CreatedAt.Value);

            await service.RemoveVolumeAsync(name, TestToken);

            Assert.DoesNotContain(await service.ListVolumesAsync(TestToken), candidate => candidate.Name == name);
        }
        finally
        {
            try
            {
                await client.Volumes.RemoveAsync(name, true, CancellationToken.None);
            }
            catch (DockerApiException)
            {
            }
        }
    }

    [Fact]
    public async Task RemovingAVolumeThatDoesNotExist_ThrowsTheEnginesError()
    {
        RequireEngine();
        using var service = new DockerService();

        var exception = await Assert.ThrowsAnyAsync<DockerApiException>(
            () => service.RemoveVolumeAsync($"windocker-tests-missing-{Guid.NewGuid():N}", TestToken));

        Assert.Equal(System.Net.HttpStatusCode.NotFound, exception.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(DockerFormat.DaemonMessage(exception.ResponseBody)));
    }

    [Fact]
    public async Task ListingImagesAndContainers_Works()
    {
        RequireEngine();
        using var service = new DockerService();

        var images = await service.ListImagesAsync(TestToken);
        var all = await service.ListContainersAsync(all: true, TestToken);
        var running = await service.ListContainersAsync(all: false, TestToken);

        Assert.All(images, image =>
        {
            Assert.False(string.IsNullOrEmpty(image.Id));
            Assert.False(string.IsNullOrEmpty(image.Reference));
            Assert.False(string.IsNullOrEmpty(image.SizeText));
        });
        Assert.All(all, container =>
        {
            Assert.False(string.IsNullOrEmpty(container.Id));
            Assert.False(string.IsNullOrEmpty(container.State));
        });
        Assert.All(running, container => Assert.Contains(all, other => other.Id == container.Id));
    }

    [Fact]
    public async Task Images_AreListedPerTagAndRemovedByReference()
    {
        var (client, image) = await RequireLinuxEngineAsync();
        using var service = new DockerService();
        var repository = $"windocker-tests/{Guid.NewGuid():N}";
        await client.Images.TagImageAsync(image, new ImageTagParameters { RepositoryName = repository, Tag = "v1" }, TestToken);
        try
        {
            var images = await service.ListImagesAsync(TestToken);

            var original = Assert.Single(images, candidate => candidate.Reference == image);
            var tagged = Assert.Single(images, candidate => candidate.Reference == $"{repository}:v1");
            Assert.Equal(original.Id, tagged.Id);
            Assert.True(tagged.SizeBytes > 0);
            Assert.NotEqual(string.Empty, tagged.SizeText);

            await service.RemoveImageAsync(tagged.Reference, TestToken);

            var remaining = await service.ListImagesAsync(TestToken);
            Assert.DoesNotContain(remaining, candidate => candidate.Reference == $"{repository}:v1");
            Assert.Contains(remaining, candidate => candidate.Reference == image);
        }
        finally
        {
            try
            {
                await client.Images.DeleteImageAsync($"{repository}:v1", new ImageDeleteParameters(), CancellationToken.None);
            }
            catch (DockerApiException)
            {
            }
        }
    }

    [Fact]
    public async Task Containers_CanBeListedStoppedStartedAndRemoved()
    {
        var (client, image) = await RequireLinuxEngineAsync();
        using var service = new DockerService();
        var id = await CreateContainerAsync(client, image, Quiet);
        try
        {
            var running = Assert.Single(await service.ListContainersAsync(all: false, TestToken), container => container.Id == id);
            Assert.StartsWith("windocker-tests-", running.Name);
            Assert.Equal(image, running.Image);
            Assert.False(string.IsNullOrEmpty(running.Command));
            Assert.Equal("running", running.State);
            Assert.True(running.CanStop);
            Assert.False(running.CanStart);
            Assert.True(running.RequiresForceRemove);
            AssertRecent(running.CreatedAt);

            var refusal = await Assert.ThrowsAnyAsync<DockerApiException>(() => service.RemoveContainerAsync(id, force: false, TestToken));
            Assert.Equal(System.Net.HttpStatusCode.Conflict, refusal.StatusCode);

            await service.StopContainerAsync(id, TestToken);

            // The engine's container list can trail a stop that has already returned (Docker before 28.3), so wait for it instead of asserting at once.
            await WaitUntilAsync(
                async () =>
                    (await service.ListContainersAsync(all: false, TestToken)).All(container => container.Id != id)
                    && (await service.ListContainersAsync(all: true, TestToken)).Any(container => container.Id == id && container.State == "exited"),
                "the stopped container to leave the running list and show as exited in the full list",
                TimeSpan.FromSeconds(10));

            Assert.DoesNotContain(await service.ListContainersAsync(all: false, TestToken), container => container.Id == id);
            var stopped = Assert.Single(await service.ListContainersAsync(all: true, TestToken), container => container.Id == id);
            Assert.Equal("exited", stopped.State);
            Assert.True(stopped.CanStart);
            Assert.False(stopped.RequiresForceRemove);

            await service.StartContainerAsync(id, TestToken);

            Assert.Equal("running", Assert.Single(await service.ListContainersAsync(all: true, TestToken), container => container.Id == id).State);

            await service.RemoveContainerAsync(id, force: true, TestToken);

            Assert.DoesNotContain(await service.ListContainersAsync(all: true, TestToken), container => container.Id == id);
        }
        finally
        {
            await RemoveQuietlyAsync(client, id);
        }
    }

    [Fact]
    public async Task Containers_ThatDoNotExist_ThrowTheEnginesError()
    {
        RequireEngine();
        using var service = new DockerService();

        var exception = await Assert.ThrowsAnyAsync<DockerApiException>(() => service.StartContainerAsync("windocker-tests-missing", TestToken));

        Assert.Equal(System.Net.HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Contains("No such container", DockerFormat.DaemonMessage(exception.ResponseBody));
    }

    [Fact]
    public async Task Logs_OfAFinishedContainerHaveTheRightOrderAndStreams()
    {
        var (client, image) = await RequireLinuxEngineAsync();
        using var service = new DockerService();
        var id = await CreateContainerAsync(client, image, Chatty(2));
        try
        {
            using var timeout = TimeoutAfter(TimeSpan.FromSeconds(60));
            await client.Containers.WaitContainerAsync(id, timeout.Token);

            var lines = await ReadAsync(service, id, tail: 100, follow: false, timeout.Token);

            Assert.Equal(
                [("out-1", false), ("err-1", true), ("out-2", false), ("err-2", true)],
                lines.Select(line => (line.Text, line.IsError)));
            Assert.All(lines, line => Assert.NotNull(line.Timestamp));
            Assert.Equal(lines.Select(line => line.Timestamp), lines.Select(line => line.Timestamp).Order());
            AssertRecent(lines[0].Timestamp!.Value);

            var lastTwo = await ReadAsync(service, id, tail: 2, follow: false, timeout.Token);

            Assert.Equal(["out-2", "err-2"], lastTwo.Select(line => line.Text));
        }
        finally
        {
            await RemoveQuietlyAsync(client, id);
        }
    }

    [Fact]
    public async Task Logs_CanBeFollowedUntilTheContainerExits()
    {
        var (client, image) = await RequireLinuxEngineAsync();
        using var service = new DockerService();
        var id = await CreateContainerAsync(client, image, Chatty(2));
        try
        {
            using var timeout = TimeoutAfter(TimeSpan.FromSeconds(60));
            var arrivals = new List<(string Text, TimeSpan At)>();
            var clock = Stopwatch.StartNew();

            await foreach (var line in service.StreamLogsAsync(id, tail: 100, follow: true, timeout.Token))
            {
                arrivals.Add((line.Text, clock.Elapsed));
            }

            Assert.Equal(["out-1", "err-1", "out-2", "err-2"], arrivals.Select(arrival => arrival.Text));
            // The lines are one second apart; they arrive as they are written, not all at the end.
            Assert.True(arrivals[^1].At - arrivals[0].At >= TimeSpan.FromSeconds(1.5), "Lines were not streamed as they were written.");
        }
        finally
        {
            await RemoveQuietlyAsync(client, id);
        }
    }

    [Fact]
    public async Task Logs_OfATtyContainerAreReadable()
    {
        var (client, image) = await RequireLinuxEngineAsync();
        using var service = new DockerService();
        var id = await CreateContainerAsync(client, image, ["sh", "-c", "echo tty-out; echo tty-err >&2; sleep 1; echo tty-last"], tty: true);
        try
        {
            using var timeout = TimeoutAfter(TimeSpan.FromSeconds(60));

            var followed = await ReadAsync(service, id, tail: 100, follow: true, timeout.Token);
            var snapshot = await ReadAsync(service, id, tail: 100, follow: false, timeout.Token);

            foreach (var lines in new[] { followed, snapshot })
            {
                Assert.Equal(["tty-out", "tty-err", "tty-last"], lines.Select(line => line.Text));
                // A TTY has a single stream, so nothing is reported as stderr.
                Assert.All(lines, line =>
                {
                    Assert.False(line.IsError);
                    Assert.NotNull(line.Timestamp);
                });
            }
        }
        finally
        {
            await RemoveQuietlyAsync(client, id);
        }
    }

    [Fact]
    public async Task Requests_ThatExceedTheClientTimeout_AreReportedAsUnavailable()
    {
        RequireEngine();
        using var service = new DockerService(() => new DockerClientBuilder().WithTimeout(TimeSpan.Zero).Build());

        var exception = await Assert.ThrowsAsync<DockerUnavailableException>(() => service.ListContainersAsync(all: true, TestToken));

        Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
    }

    [Fact]
    public async Task Logs_FollowedForLongerThanTheClientTimeout_KeepStreaming()
    {
        var (client, image) = await RequireLinuxEngineAsync();
        var id = await CreateContainerAsync(client, image, Chatty(3));
        try
        {
            using var service = new DockerService(() => new DockerClientBuilder().WithTimeout(TimeSpan.FromSeconds(2)).Build());
            using var timeout = TimeoutAfter(TimeSpan.FromSeconds(60));
            var clock = Stopwatch.StartNew();

            var lines = await ReadAsync(service, id, tail: 100, follow: true, timeout.Token);

            Assert.Equal(["out-1", "err-1", "out-2", "err-2", "out-3", "err-3"], lines.Select(line => line.Text));
            Assert.True(clock.Elapsed > TimeSpan.FromSeconds(4), $"The stream ended after {clock.Elapsed}, before the container was done.");
        }
        finally
        {
            await RemoveQuietlyAsync(client, id);
        }
    }

    [Fact]
    public async Task Logs_CancellingAFollowStreamEndsItPromptly()
    {
        var (client, image) = await RequireLinuxEngineAsync();
        using var service = new DockerService();
        var id = await CreateContainerAsync(client, image, Quiet);
        try
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestToken);
            var clock = Stopwatch.StartNew();

            var reading = Task.Run(() => ReadAsync(service, id, tail: 100, follow: true, cancellation.Token), TestToken);
            await Task.Delay(TimeSpan.FromSeconds(1), TestToken);
            Assert.False(reading.IsCompleted);
            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading.WaitAsync(TimeSpan.FromSeconds(10), TestToken));
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10));
        }
        finally
        {
            await RemoveQuietlyAsync(client, id);
        }
    }

    [Fact]
    public async Task Logs_OfAContainerThatDoesNotExist_ThrowTheEnginesError()
    {
        RequireEngine();
        using var service = new DockerService();

        var exception = await Assert.ThrowsAnyAsync<DockerApiException>(
            () => ReadAsync(service, "windocker-tests-missing", tail: 10, follow: false, TestToken));

        Assert.Equal(System.Net.HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public Task LogsViewModel_LoadsASnapshotFromARealContainer() => UiThread.RunAsync(async () =>
    {
        var (client, image) = await RequireLinuxEngineAsync();
        using var service = new DockerService();
        var id = await CreateContainerAsync(client, image, Chatty(1));
        try
        {
            using var timeout = TimeoutAfter(TimeSpan.FromSeconds(60));
            await client.Containers.WaitContainerAsync(id, timeout.Token);
            var viewModel = new LogsViewModel(service, new FakeLocalizer(), TimeProvider.System);
            viewModel.Initialize(id, "test");

            await viewModel.RefreshCommand.ExecuteAsync(null);

            Assert.Equal(["out-1", "err-1"], viewModel.VisibleLines.Select(line => line.Text));
            Assert.Equal([false, true], viewModel.VisibleLines.Select(line => line.IsError));
            Assert.False(viewModel.HasError);
            Assert.False(viewModel.IsBusy);
        }
        finally
        {
            await RemoveQuietlyAsync(client, id);
        }
    });

    [Fact]
    public Task LogsViewModel_FollowsARealContainerUntilItExits() => UiThread.RunAsync(async () =>
    {
        var (client, image) = await RequireLinuxEngineAsync();
        using var service = new DockerService();
        var id = await CreateContainerAsync(client, image, Chatty(2));
        try
        {
            var viewModel = new LogsViewModel(service, new FakeLocalizer(), TimeProvider.System);
            viewModel.Initialize(id, "test");
            var ended = new TaskCompletionSource();
            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LogsViewModel.IsFollowing) && !viewModel.IsFollowing)
                {
                    ended.TrySetResult();
                }
            };
            var appendedBeforeTheEnd = 0;
            viewModel.LinesAppended += (_, _) =>
            {
                if (!ended.Task.IsCompleted)
                {
                    appendedBeforeTheEnd++;
                }
            };

            viewModel.IsFollowing = true;
            await ended.Task.WaitAsync(TimeSpan.FromSeconds(60), TestToken);

            Assert.Equal(["out-1", "err-1", "out-2", "err-2"], viewModel.VisibleLines.Select(line => line.Text));
            Assert.Equal([false, true, false, true], viewModel.VisibleLines.Select(line => line.IsError));
            Assert.False(viewModel.HasError);
            // The container prints over about three seconds, so several flushes happen while it runs.
            Assert.True(appendedBeforeTheEnd >= 2, $"Only {appendedBeforeTheEnd} flushes happened while following.");
        }
        finally
        {
            await RemoveQuietlyAsync(client, id);
        }
    });

    [Fact]
    public Task LogsViewModel_ReportsAContainerThatDoesNotExist() => UiThread.RunAsync(async () =>
    {
        RequireEngine();
        using var service = new DockerService();
        var viewModel = new LogsViewModel(service, new FakeLocalizer(), TimeProvider.System);
        viewModel.Initialize("windocker-tests-missing", "missing");

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Contains("No such container", viewModel.ErrorMessage);
        Assert.Empty(viewModel.VisibleLines);
    });
}
