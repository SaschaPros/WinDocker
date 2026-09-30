using CommunityToolkit.Mvvm.ComponentModel;

namespace WinDocker.Core.Models;

/// <summary>A row of the volume list; the instance stays the same while the volume exists (see <see cref="ContainerItem"/>).</summary>
public sealed partial class VolumeItem : ObservableObject
{
    public VolumeItem(VolumeInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        Name = info.Name;
        Info = info;
    }

    public string Name { get; }

    [ObservableProperty]
    public partial VolumeInfo Info { get; private set; }

    /// <summary>Takes over <paramref name="info"/>. Nothing is raised when it equals the current value (records compare by value).</summary>
    public void Update(VolumeInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        Info = info;
    }
}
