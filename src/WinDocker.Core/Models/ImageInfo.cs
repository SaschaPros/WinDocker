using System.Globalization;
using WinDocker.Core.Docker;

namespace WinDocker.Core.Models;

/// <summary>One row of <c>docker images</c>: an image with a single repository and tag.</summary>
public sealed record ImageInfo(string Id, string Repository, string Tag, DateTimeOffset CreatedAt, long SizeBytes)
{
    public string ShortId => DockerFormat.ShortId(Id);

    /// <summary>True when the row has no usable repository:tag (dangling or referenced by digest only).</summary>
    public bool IsUntagged => Repository == DockerFormat.NoneMarker || Tag == DockerFormat.NoneMarker;

    /// <summary>What to pass to a removal request: <c>repository:tag</c>, or the full image ID for untagged rows.</summary>
    public string Reference => IsUntagged ? Id : $"{Repository}:{Tag}";

    public string SizeText => DockerFormat.Size(SizeBytes);

    public string CreatedText => CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
}
