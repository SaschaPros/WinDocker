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
