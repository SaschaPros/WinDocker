using System.Globalization;
using Docker.DotNet.Models;
using WinDocker.Core.Docker;
using WinDocker.Core.Models;
using VolumeInfo = WinDocker.Core.Models.VolumeInfo;

namespace WinDocker.Core.Services;

/// <summary>Maps Docker client models to the Core models.</summary>
internal static class DockerMapper
{
    private const string DigestPrefix = "sha256:";

    public static ContainerInfo ToContainerInfo(ContainerListResponse container) => new(
        container.ID,
        DockerFormat.ContainerName(container.Names),
        container.Image.StartsWith(DigestPrefix, StringComparison.Ordinal) ? DockerFormat.ShortId(container.Image) : container.Image,
        container.Command,
        ToDateTimeOffset(container.Created),
        container.State,
        container.Status,
        DockerFormat.Ports(container.Ports?.Select(port => new PortMapping(port.IP, port.PrivatePort, port.PublicPort, port.Type))));

    /// <summary>
    /// Expands an image into one row per repository:tag like <c>docker images</c>. An image that is only known
    /// by digest yields a "&lt;none&gt;" tag row per repository, and a dangling image a single "&lt;none&gt;:&lt;none&gt;" row.
    /// </summary>
    public static IEnumerable<ImageInfo> ToImageInfos(ImagesListResponse image)
    {
        var created = ToDateTimeOffset(image.Created);
        var rows = new List<ImageInfo>();
        var repositories = new HashSet<string>(StringComparer.Ordinal);

        foreach (var repoTag in image.RepoTags ?? [])
        {
            if (TrySplitRepoTag(repoTag, out var repository, out var tag))
            {
                rows.Add(new ImageInfo(image.ID, repository, tag, created, image.Size));
                repositories.Add(repository);
            }
        }

        foreach (var repoDigest in image.RepoDigests ?? [])
        {
            var separator = repoDigest.IndexOf('@');
            if (separator > 0 && repositories.Add(repoDigest[..separator]))
            {
                rows.Add(new ImageInfo(image.ID, repoDigest[..separator], DockerFormat.NoneMarker, created, image.Size));
            }
        }

        if (rows.Count == 0)
        {
            rows.Add(new ImageInfo(image.ID, DockerFormat.NoneMarker, DockerFormat.NoneMarker, created, image.Size));
        }

        return rows;
    }

    public static VolumeInfo ToVolumeInfo(VolumeResponse volume) => new(
        volume.Name,
        volume.Driver,
        volume.Mountpoint,
        DateTimeOffset.TryParse(volume.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var createdAt)
            ? createdAt
            : null);

    private static bool TrySplitRepoTag(string repoTag, out string repository, out string tag)
    {
        repository = string.Empty;
        tag = string.Empty;

        // "host:5000/name:tag": the tag separator is the last ':' after the last '/'.
        var colon = repoTag.LastIndexOf(':');
        if (colon <= repoTag.LastIndexOf('/'))
        {
            return false;
        }

        repository = repoTag[..colon];
        tag = repoTag[(colon + 1)..];
        return repository != DockerFormat.NoneMarker && tag != DockerFormat.NoneMarker;
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value);
}
