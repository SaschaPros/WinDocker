namespace WinDocker.Core.Models;

/// <summary>
/// Identity of an image row. The image ID alone is not enough because one image has a row per repository:tag,
/// and repository:tag alone is not enough because images that are only known by digest have none.
/// </summary>
public readonly record struct ImageKey(string Id, string Repository, string Tag)
{
    public static ImageKey From(ImageInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        return new ImageKey(info.Id, info.Repository, info.Tag);
    }
}
