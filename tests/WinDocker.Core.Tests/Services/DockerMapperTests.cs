using Docker.DotNet.Models;
using WinDocker.Core.Models;
using WinDocker.Core.Services;

namespace WinDocker.Core.Tests.Services;

public class DockerMapperTests
{
    private static readonly DateTime CreatedUtc = new(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);

    private static ContainerListResponse Container(Action<ContainerListResponse>? configure = null)
    {
        var container = new ContainerListResponse
        {
            ID = "0123456789abcdef0123456789abcdef",
            Names = ["/web"],
            Image = "nginx:latest",
            Command = "nginx -g 'daemon off;'",
            Created = CreatedUtc,
            State = "running",
            Status = "Up 2 hours",
            Ports = [],
        };
        configure?.Invoke(container);
        return container;
    }

    private static ImagesListResponse Image(IList<string>? repoTags, IList<string>? repoDigests = null) => new()
    {
        ID = "sha256:0123456789abcdef0123456789abcdef",
        Created = CreatedUtc,
        Size = 142_000_000,
        RepoTags = repoTags!,
        RepoDigests = repoDigests!,
    };

    [Fact]
    public void ToContainerInfo_MapsTheFields()
    {
        var info = DockerMapper.ToContainerInfo(Container(container =>
        {
            container.Ports =
            [
                new PortSummary { IP = "0.0.0.0", PrivatePort = 80, PublicPort = 8080, Type = "tcp" },
                new PortSummary { PrivatePort = 443, Type = "tcp" },
            ];
        }));

        Assert.Equal("0123456789abcdef0123456789abcdef", info.Id);
        Assert.Equal("web", info.Name);
        Assert.Equal("nginx:latest", info.Image);
        Assert.Equal("nginx -g 'daemon off;'", info.Command);
        Assert.Equal(new DateTimeOffset(CreatedUtc), info.CreatedAt);
        Assert.Equal(TimeSpan.Zero, info.CreatedAt.Offset);
        Assert.Equal("running", info.State);
        Assert.Equal("Up 2 hours", info.Status);
        Assert.Equal("0.0.0.0:8080->80/tcp, 443/tcp", info.Ports);
    }

    [Fact]
    public void ToContainerInfo_ToleratesMissingNamesAndPorts()
    {
        var info = DockerMapper.ToContainerInfo(Container(container =>
        {
            container.Names = null!;
            container.Ports = null!;
        }));

        Assert.Equal(string.Empty, info.Name);
        Assert.Equal(string.Empty, info.Ports);
    }

    [Fact]
    public void ToContainerInfo_ShowsTheShortIdWhenTheImageIsAnId()
    {
        var info = DockerMapper.ToContainerInfo(Container(container => container.Image = "sha256:0123456789abcdef0123456789abcdef"));

        Assert.Equal("0123456789ab", info.Image);
    }

    [Fact]
    public void ToContainerInfo_TreatsAnUnspecifiedTimeAsUtc()
    {
        var info = DockerMapper.ToContainerInfo(Container(container => container.Created = new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Unspecified)));

        Assert.Equal(new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero), info.CreatedAt);
    }

    [Fact]
    public void ToImageInfos_ReturnsOneRowPerTag()
    {
        var rows = DockerMapper.ToImageInfos(Image(["nginx:1.27", "nginx:latest", "registry.example.com:5000/team/nginx:v2"])).ToList();

        Assert.Equal(
            [("nginx", "1.27"), ("nginx", "latest"), ("registry.example.com:5000/team/nginx", "v2")],
            rows.Select(row => (row.Repository, row.Tag)));
        Assert.All(rows, row =>
        {
            Assert.Equal("sha256:0123456789abcdef0123456789abcdef", row.Id);
            Assert.Equal(142_000_000, row.SizeBytes);
            Assert.Equal(new DateTimeOffset(CreatedUtc), row.CreatedAt);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ToImageInfos_ReturnsADanglingRowWithoutTagsOrDigests(string? tag)
    {
        var rows = DockerMapper.ToImageInfos(Image(tag is null ? null : [], tag is null ? null : []));

        var row = Assert.Single(rows);
        Assert.Equal(("<none>", "<none>"), (row.Repository, row.Tag));
        Assert.True(row.IsUntagged);
        Assert.Equal("sha256:0123456789abcdef0123456789abcdef", row.Reference);
    }

    [Fact]
    public void ToImageInfos_IgnoresTheLegacyNoneMarkerTag()
    {
        var row = Assert.Single(DockerMapper.ToImageInfos(Image(["<none>:<none>"])));

        Assert.Equal(("<none>", "<none>"), (row.Repository, row.Tag));
    }

    [Fact]
    public void ToImageInfos_ReturnsARowPerRepositoryForImagesKnownByDigestOnly()
    {
        var rows = DockerMapper.ToImageInfos(Image([], ["nginx@sha256:aaa", "nginx@sha256:bbb", "redis@sha256:ccc"])).ToList();

        Assert.Equal([("nginx", "<none>"), ("redis", "<none>")], rows.Select(row => (row.Repository, row.Tag)));
    }

    [Fact]
    public void ToImageInfos_DoesNotRepeatARepositoryThatHasTagsForItsDigest()
    {
        var rows = DockerMapper.ToImageInfos(Image(["nginx:latest"], ["nginx@sha256:aaa"])).ToList();

        var row = Assert.Single(rows);
        Assert.Equal("nginx:latest", row.Reference);
    }

    [Fact]
    public void ToVolumeInfo_MapsTheFieldsAndParsesTheCreationTime()
    {
        var info = DockerMapper.ToVolumeInfo(new VolumeResponse
        {
            Name = "data",
            Driver = "local",
            Mountpoint = "/var/lib/docker/volumes/data/_data",
            CreatedAt = "2026-09-29T12:00:00+02:00",
        });

        Assert.Equal("data", info.Name);
        Assert.Equal("local", info.Driver);
        Assert.Equal("/var/lib/docker/volumes/data/_data", info.Mountpoint);
        Assert.Equal(new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero), info.CreatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a date")]
    public void ToVolumeInfo_HasNoCreationTimeWhenItIsMissingOrInvalid(string? createdAt)
    {
        var info = DockerMapper.ToVolumeInfo(new VolumeResponse { Name = "data", CreatedAt = createdAt });

        Assert.Null(info.CreatedAt);
    }

    [Fact]
    public void ToVolumeInfo_TreatsATimeWithoutOffsetAsUtc()
    {
        var info = DockerMapper.ToVolumeInfo(new VolumeResponse { Name = "data", CreatedAt = "2026-09-29T10:00:00" });

        Assert.Equal(new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero), info.CreatedAt);
    }
}
