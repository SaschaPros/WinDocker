using WinDocker.Core.Settings;

namespace WinDocker.Core.Columns;

/// <summary>The layouts of the four list pages. Meant to be a singleton, so a page and the view model behind it share one layout.</summary>
public sealed class ListLayouts
{
    public const string ContainersKey = "containers";
    public const string ComposeKey = "compose";
    public const string ImagesKey = "images";
    public const string VolumesKey = "volumes";

    public ListLayouts(SettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Containers = new ListLayout(ContainersKey, Keys(ContainerColumns.All), settings);
        Compose = new ListLayout(ComposeKey, Keys(ComposeColumns.All), settings);
        Images = new ListLayout(ImagesKey, Keys(ImageColumns.All), settings);
        Volumes = new ListLayout(VolumesKey, Keys(VolumeColumns.All), settings);
    }

    public ListLayout Containers { get; }

    public ListLayout Compose { get; }

    public ListLayout Images { get; }

    public ListLayout Volumes { get; }

    private static string[] Keys(IEnumerable<IColumnDefinition> columns) => columns.Select(column => column.Key).ToArray();
}
