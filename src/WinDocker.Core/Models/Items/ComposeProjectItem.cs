using CommunityToolkit.Mvvm.ComponentModel;

namespace WinDocker.Core.Models;

/// <summary>A row of the compose project list; the instance stays the same while the project exists (see <see cref="ContainerItem"/>).</summary>
public sealed partial class ComposeProjectItem : ObservableObject
{
    public ComposeProjectItem(ComposeProjectInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        Name = info.Name;
        Info = info;
    }

    public string Name { get; }

    [ObservableProperty]
    public partial ComposeProjectInfo Info { get; private set; }

    /// <summary>Localized summary of the containers' states, for example "Running (2/3)". Kept up to date by the view model.</summary>
    [ObservableProperty]
    public partial string StatusText { get; internal set; } = string.Empty;

    /// <summary>Takes over <paramref name="info"/>. Nothing is raised when it equals the current value.</summary>
    public void Update(ComposeProjectInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        Info = info;
    }
}
