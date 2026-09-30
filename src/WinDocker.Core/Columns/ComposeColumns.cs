using WinDocker.Core.Models;

namespace WinDocker.Core.Columns;

/// <summary>The columns of the compose project list, in their default order.</summary>
public static class ComposeColumns
{
    public const string Project = "project";
    public const string Status = "status";
    public const string Services = "services";
    public const string WorkingDir = "workingDir";
    public const string ConfigFiles = "configFiles";

    public static IReadOnlyList<ListColumn<ComposeProjectInfo>> All { get; } =
    [
        new(Project, "Compose_Column_Project", ColumnWidth.Star(1.5), ColumnComparers.Text<ComposeProjectInfo>(info => info.Name)),
        new(Status, "Compose_Column_Status", ColumnWidth.Star(1.5), CompareStatus),
        new(Services, "Compose_Column_Services", ColumnWidth.Star(2), ColumnComparers.Text<ComposeProjectInfo>(info => info.ServicesText)),
        new(WorkingDir, "Compose_Column_WorkingDir", ColumnWidth.Star(3), ColumnComparers.Text<ComposeProjectInfo>(info => info.WorkingDir)),
        new(ConfigFiles, "Compose_Column_ConfigFiles", ColumnWidth.Star(3), ColumnComparers.Text<ComposeProjectInfo>(info => info.ConfigFiles)),
    ];

    /// <summary>
    /// Not the localized status text but its meaning: none running, then some running, then all running; within a group by
    /// the number of running containers and then the total. The text would order "Exited" before "Partial (9/9)" only by chance of the language.
    /// </summary>
    private static int CompareStatus(ComposeProjectInfo left, ComposeProjectInfo right)
    {
        var byGroup = Group(left).CompareTo(Group(right));
        if (byGroup != 0)
        {
            return byGroup;
        }

        var byRunning = left.RunningCount.CompareTo(right.RunningCount);
        return byRunning != 0 ? byRunning : left.TotalCount.CompareTo(right.TotalCount);
    }

    private static int Group(ComposeProjectInfo info) =>
        info.RunningCount == 0 ? 0 : info.RunningCount == info.TotalCount ? 2 : 1;
}
