using WinDocker.Core.Models;

namespace WinDocker.Core.Tests.Models;

public class ModelTests
{
    private static readonly DateTimeOffset Instant = new(2026, 9, 29, 10, 15, 30, 123, TimeSpan.Zero);

    /// <summary>A culture whose "g" format is known, whatever the ICU data of the machine says.</summary>
    private static CultureInfo TestCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.ShortDatePattern = "dd.MM.yyyy";
        culture.DateTimeFormat.ShortTimePattern = "HH:mm";
        return culture;
    }

    private static void WithCulture(CultureInfo culture, Action action)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static ContainerInfo Container(string state = "running", string id = "sha256:0123456789abcdef0123") =>
        new(id, "web", "nginx", "nginx -g", Instant, state, "Up 2 hours", "80/tcp");

    [Fact]
    public void ContainerInfo_ComputesShortIdAndLocalCreatedText() =>
        WithCulture(TestCulture(), () =>
        {
            var container = Container();

            Assert.Equal("0123456789ab", container.ShortId);
            Assert.Equal(Instant.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture), container.CreatedText);
        });

    [Theory]
    [InlineData("running", false, true, true)]
    [InlineData("exited", true, false, false)]
    [InlineData("created", true, false, false)]
    [InlineData("paused", false, true, true)]
    [InlineData("dead", false, false, false)]
    public void ContainerInfo_DerivesActionsFromTheState(string state, bool canStart, bool canStop, bool requiresForce)
    {
        var container = Container(state);

        Assert.Equal(canStart, container.CanStart);
        Assert.Equal(canStop, container.CanStop);
        Assert.Equal(requiresForce, container.RequiresForceRemove);
    }

    [Theory]
    [InlineData("shop", "web", "shop / web")]
    [InlineData("shop", "", "shop")]
    public void ContainerInfo_ProjectTextJoinsTheProjectAndTheService(string project, string service, string expected) =>
        Assert.Equal(expected, (Container() with { Compose = new ComposeLabels(project, service, null, null, false) }).ProjectText);

    [Fact]
    public void ContainerInfo_ProjectTextIsEmptyWithoutAComposeProject()
    {
        Assert.Null(Container().Compose);
        Assert.Equal(string.Empty, Container().ProjectText);
    }

    [Fact]
    public void ContainerInfo_IsEqualWhenTheComposeLabelsAreEqual()
    {
        var first = Container() with { Compose = new ComposeLabels("shop", "web", "/srv/shop", "/srv/shop/compose.yaml", false) };
        var second = Container() with { Compose = new ComposeLabels("shop", "web", "/srv/shop", "/srv/shop/compose.yaml", false) };

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, first with { Compose = first.Compose! with { IsOneOff = true } });
        Assert.NotEqual(first, Container());
    }

    [Theory]
    [InlineData("running", false, true)]
    [InlineData("exited", true, false)]
    [InlineData("created", true, false)]
    [InlineData("paused", false, true)]
    [InlineData("restarting", false, true)]
    [InlineData("dead", false, false)]
    [InlineData("removing", false, false)]
    public void ComposeContainer_DerivesActionsFromTheState(string state, bool canStart, bool canStop)
    {
        var container = new ComposeContainer("c1", "shop-web-1", "web", state, Instant, false);

        Assert.Equal(canStart, container.CanStart);
        Assert.Equal(canStop, container.CanStop);
    }

    private static ComposeProjectInfo Project() => new(
        "shop",
        "/srv/shop",
        "/srv/shop/compose.yaml",
        ["db", "web"],
        1,
        2,
        [
            new ComposeContainer("c1", "shop-db-1", "db", "running", Instant, false),
            new ComposeContainer("c2", "shop-web-1", "web", "exited", Instant.AddSeconds(1), false),
        ],
        Instant);

    [Fact]
    public void ComposeProjectInfo_IsEqualWhenTheListsHaveTheSameContents()
    {
        var first = Project();
        var second = Project();

        Assert.NotSame(first.Services, second.Services);
        Assert.NotSame(first.Containers, second.Containers);
        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.False(first.Equals(null));
    }

    [Fact]
    public void ComposeProjectInfo_DiffersWhenAnyMemberDiffers()
    {
        var project = Project();

        Assert.NotEqual(project, project with { Name = "blog" });
        Assert.NotEqual(project, project with { WorkingDir = "/srv/other" });
        Assert.NotEqual(project, project with { WorkingDir = null });
        Assert.NotEqual(project, project with { ConfigFiles = null });
        Assert.NotEqual(project, project with { RunningCount = 2 });
        Assert.NotEqual(project, project with { TotalCount = 3 });
        Assert.NotEqual(project, project with { CreatedAt = Instant.AddSeconds(1) });
        Assert.NotEqual(project, project with { Services = ["db"] });
        Assert.NotEqual(project, project with { Services = ["web", "db"] });
        Assert.NotEqual(project, project with { Containers = [project.Containers[0]] });
        Assert.NotEqual(project, project with { Containers = [.. project.Containers.Reverse()] });
        Assert.NotEqual(project, project with { Containers = [project.Containers[0] with { State = "exited" }, project.Containers[1]] });
    }

    [Fact]
    public void ComposeProjectInfo_JoinsTheServicesIntoText()
    {
        Assert.Equal("db, web", Project().ServicesText);
        Assert.Equal(string.Empty, (Project() with { Services = [] }).ServicesText);
    }

    [Fact]
    public void ComposeProjectInfo_CanStartAndStopFollowTheStatesOfItsContainers()
    {
        var mixed = Project();
        var running = mixed with { Containers = [mixed.Containers[0]] };
        var exited = mixed with { Containers = [mixed.Containers[1]] };
        var none = mixed with { Containers = [] };

        Assert.True(mixed.CanStart);
        Assert.True(mixed.CanStop);
        Assert.False(running.CanStart);
        Assert.True(running.CanStop);
        Assert.True(exited.CanStart);
        Assert.False(exited.CanStop);
        Assert.False(none.CanStart);
        Assert.False(none.CanStop);
    }

    [Fact]
    public void ComposeProjectInfo_LeavesOneOffContainersOutOfWhatCanBeStartedAndStopped()
    {
        var project = Project();
        var oneOffs = project with
        {
            Containers =
            [
                new ComposeContainer("r1", "shop-web-run-1", "web", "exited", Instant, true),
                new ComposeContainer("r2", "shop-web-run-2", "web", "running", Instant, true),
            ],
        };
        var withService = oneOffs with { Containers = [.. oneOffs.Containers, project.Containers[0]] };

        Assert.False(oneOffs.CanStart);
        Assert.False(oneOffs.CanStop);
        Assert.False(oneOffs.HasServiceContainers);
        Assert.False(withService.CanStart);
        Assert.True(withService.CanStop);
        Assert.True(withService.HasServiceContainers);
        Assert.False((project with { Containers = [] }).HasServiceContainers);
    }

    [Fact]
    public void PruneResult_IsEqualByValue() =>
        Assert.Equal(new PruneResult(2, 1_500_000), new PruneResult(2, 1_500_000));

    [Fact]
    public void ImageInfo_UsesRepositoryAndTagAsReference()
    {
        var image = new ImageInfo("sha256:0123456789abcdef", "nginx", "1.27", Instant, 142_000_000);

        Assert.False(image.IsUntagged);
        Assert.Equal("nginx:1.27", image.Reference);
        Assert.Equal("0123456789ab", image.ShortId);
        Assert.Equal("142MB", image.SizeText);
    }

    [Fact]
    public void ImageInfo_KeepsTheRegistryPortInTheReference() =>
        Assert.Equal(
            "localhost:5000/team/app:v1",
            new ImageInfo("sha256:abc", "localhost:5000/team/app", "v1", Instant, 1).Reference);

    [Theory]
    [InlineData("<none>", "<none>")]
    [InlineData("nginx", "<none>")]
    [InlineData("<none>", "latest")]
    public void ImageInfo_UsesTheFullIdAsReferenceWhenUntagged(string repository, string tag)
    {
        var image = new ImageInfo("sha256:0123456789abcdef", repository, tag, Instant, 1);

        Assert.True(image.IsUntagged);
        Assert.Equal("sha256:0123456789abcdef", image.Reference);
    }

    [Fact]
    public void ImageInfo_FormatsTheLocalCreationTime() =>
        WithCulture(TestCulture(), () =>
        {
            var image = new ImageInfo("sha256:abc", "nginx", "latest", Instant, 1);

            Assert.Equal(Instant.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture), image.CreatedText);
        });

    [Fact]
    public void VolumeInfo_FormatsTheCreationTimeOrIsEmpty() =>
        WithCulture(TestCulture(), () =>
        {
            Assert.Equal(
                Instant.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture),
                new VolumeInfo("data", "local", "/var/lib/docker/volumes/data/_data", Instant).CreatedText);
            Assert.Equal(string.Empty, new VolumeInfo("data", "local", "/mnt", null).CreatedText);
        });

    [Fact]
    public void LogLine_FormatsTheTimestampInLocalTimeWithMilliseconds()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.TimeSeparator = "-";

        WithCulture(culture, () =>
            Assert.Equal(
                Instant.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                new LogLine(Instant, "hello", false).TimestampText));
    }

    [Fact]
    public void LogLine_TimestampTextIsEmptyWithoutTimestamp() =>
        Assert.Equal(string.Empty, new LogLine(null, "hello", false).TimestampText);
}
