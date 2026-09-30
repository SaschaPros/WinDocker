using CommunityToolkit.Mvvm.ComponentModel;

namespace WinDocker.Core.Models;

/// <summary>
/// A row of the container list. The instance stays the same while the container exists, so a list view keeps
/// its selection; only <see cref="Info"/> changes when a refresh brings new values.
/// </summary>
public sealed partial class ContainerItem : ObservableObject
{
    public ContainerItem(ContainerInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        Id = info.Id;
        Info = info;
    }

    public string Id { get; }

    [ObservableProperty]
    public partial ContainerInfo Info { get; private set; }

    /// <summary>Takes over <paramref name="info"/>. Nothing is raised when it equals the current value (records compare by value).</summary>
    public void Update(ContainerInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        Info = info;
    }
}
