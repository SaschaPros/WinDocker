using WinDocker.Core.Models;

namespace WinDocker.Core.Columns;

/// <summary>The columns of the volume list, in their default order.</summary>
public static class VolumeColumns
{
    public const string Name = "name";
    public const string Driver = "driver";
    public const string Mountpoint = "mountpoint";
    public const string Created = "created";

    public static IReadOnlyList<ListColumn<VolumeInfo>> All { get; } =
    [
        new(Name, "Volumes_Column_Name", ColumnWidth.Star(2), ColumnComparers.Text<VolumeInfo>(info => info.Name)),
        new(Driver, "Volumes_Column_Driver", ColumnWidth.Pixels(90), ColumnComparers.Text<VolumeInfo>(info => info.Driver)),
        new(Mountpoint, "Volumes_Column_Mountpoint", ColumnWidth.Star(3), ColumnComparers.Text<VolumeInfo>(info => info.Mountpoint)),
        new(Created, "Volumes_Column_Created", ColumnWidth.Pixels(150), ColumnComparers.Value<VolumeInfo, DateTimeOffset?>(info => info.CreatedAt)),
    ];
}
