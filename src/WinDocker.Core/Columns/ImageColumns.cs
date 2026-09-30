using WinDocker.Core.Models;

namespace WinDocker.Core.Columns;

/// <summary>The columns of the image list, in their default order.</summary>
public static class ImageColumns
{
    public const string Repository = "repository";
    public const string Tag = "tag";
    public const string Id = "id";
    public const string Created = "created";
    public const string Size = "size";

    public static IReadOnlyList<ListColumn<ImageInfo>> All { get; } =
    [
        new(Repository, "Images_Column_Repository", ColumnWidth.Star(2), ColumnComparers.Text<ImageInfo>(info => info.Repository)),
        new(Tag, "Images_Column_Tag", ColumnWidth.Star(1), ColumnComparers.Text<ImageInfo>(info => info.Tag)),
        new(Id, "Images_Column_Id", ColumnWidth.Pixels(110), ColumnComparers.Text<ImageInfo>(info => info.Id)),
        new(Created, "Images_Column_Created", ColumnWidth.Pixels(150), ColumnComparers.Value<ImageInfo, DateTimeOffset>(info => info.CreatedAt)),
        new(Size, "Images_Column_Size", ColumnWidth.Pixels(100), ColumnComparers.Value<ImageInfo, long>(info => info.SizeBytes)),
    ];
}
