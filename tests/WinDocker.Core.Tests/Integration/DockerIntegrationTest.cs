using System.Diagnostics;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace WinDocker.Core.Tests.Integration;

/// <summary>
/// Plumbing shared by the tests that run against the local Docker engine (DOCKER_HOST, docker context or the default
/// pipe or socket). They skip themselves when no engine answers, and those that start Linux containers when the engine runs Windows containers.
/// The derived classes must be in the <see cref="DockerEngineCollection"/>.
/// </summary>
public abstract class DockerIntegrationTest(DockerEngineFixture engine)
{
    /// <summary>Stays quiet and exits promptly on SIGTERM (a shell as PID 1 ignores it otherwise).</summary>
    protected static readonly string[] Quiet = ["sh", "-c", "trap 'exit 0' TERM; while true; do sleep 1; done"];

    protected static CancellationToken TestToken => TestContext.Current.CancellationToken;

    protected static CancellationTokenSource TimeoutAfter(TimeSpan timeout)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(TestToken);
        source.CancelAfter(timeout);
        return source;
    }

    /// <summary>The engine may run in a VM whose clock is off by minutes, so this only rules out wrong parsing.</summary>
    protected static void AssertRecent(DateTimeOffset timestamp) =>
        Assert.InRange(timestamp, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

    /// <summary>Polls <paramref name="condition"/> every 100 ms until it holds; fails the test when it still does not after <paramref name="timeout"/>.</summary>
    protected static async Task WaitUntilAsync(Func<Task<bool>> condition, string description, TimeSpan timeout)
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

    protected IDockerClient RequireEngine()
    {
        Assert.SkipUnless(engine.Client is not null, engine.UnavailableReason ?? "No Docker engine.");
        return engine.Client!;
    }

    protected async Task<(IDockerClient Client, string Image)> RequireLinuxEngineAsync()
    {
        var client = RequireEngine();
        Assert.SkipUnless(engine.IsLinuxEngine, "The engine runs Windows containers.");
        using var timeout = TimeoutAfter(TimeSpan.FromMinutes(3));
        var image = await engine.EnsureImageAsync(timeout.Token);
        Assert.SkipWhen(image is null, "No test image could be pulled.");
        return (client, image!);
    }

    /// <summary>
    /// Creates (and by default starts) a container without network that carries <see cref="DockerEngineFixture.TestLabel"/>.
    /// <paramref name="configure"/> may change the request, for example to add labels or attach a network.
    /// </summary>
    protected static async Task<string> CreateContainerAsync(
        IDockerClient client,
        string image,
        string[] command,
        bool tty = false,
        bool start = true,
        Action<CreateContainerParameters>? configure = null)
    {
        var parameters = new CreateContainerParameters
        {
            Image = image,
            Cmd = command,
            Tty = tty,
            Name = $"windocker-tests-{Guid.NewGuid():N}",
            Labels = new Dictionary<string, string> { [DockerEngineFixture.TestLabel] = "true" },
            HostConfig = new HostConfig { NetworkMode = "none" },
        };
        configure?.Invoke(parameters);

        var created = await client.Containers.CreateContainerAsync(parameters, TestToken);
        if (start)
        {
            await client.Containers.StartContainerAsync(created.ID, null, TestToken);
        }

        return created.ID;
    }

    protected static async Task RemoveQuietlyAsync(IDockerClient client, string containerId)
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
}
