using WinDocker.Core.Models;

namespace WinDocker.Core.Columns;

/// <summary>The columns of the container list, in their default order.</summary>
public static class ContainerColumns
{
    public const string Name = "name";
    public const string Project = "project";
    public const string Image = "image";
    public const string Status = "status";
    public const string Ports = "ports";
    public const string Created = "created";
    public const string Id = "id";
    public const string Command = "command";

    public static IReadOnlyList<ListColumn<ContainerInfo>> All { get; } =
    [
        new(Name, "Containers_Column_Name", ColumnWidth.Star(1.2), ColumnComparers.Text<ContainerInfo>(info => info.Name)),
        new(Project, "Containers_Column_Project", ColumnWidth.Star(1.2), ColumnComparers.Text<ContainerInfo>(info => info.ProjectText)),
        new(Image, "Containers_Column_Image", ColumnWidth.Star(1.4), ColumnComparers.Text<ContainerInfo>(info => info.Image)),
        new(Status, "Containers_Column_Status", ColumnWidth.Star(1.3), CompareStatus),
        new(Ports, "Containers_Column_Ports", ColumnWidth.Star(1.6), ColumnComparers.Text<ContainerInfo>(info => info.Ports)),
        new(Created, "Containers_Column_Created", ColumnWidth.Pixels(150), ColumnComparers.Value<ContainerInfo, DateTimeOffset>(info => info.CreatedAt)),
        new(Id, "Containers_Column_Id", ColumnWidth.Pixels(110), ColumnComparers.Text<ContainerInfo>(info => info.Id)),
        new(Command, "Containers_Column_Command", ColumnWidth.Star(1.4), ColumnComparers.Text<ContainerInfo>(info => info.Command)),
    ];

    /// <summary>The state (running, exited, ...) first, because the status text ("Up 2 hours") only makes sense within one state.</summary>
    private static int CompareStatus(ContainerInfo left, ContainerInfo right)
    {
        var byState = StringComparer.CurrentCultureIgnoreCase.Compare(left.State, right.State);
        return byState != 0 ? byState : StringComparer.CurrentCultureIgnoreCase.Compare(left.Status, right.Status);
    }
}
