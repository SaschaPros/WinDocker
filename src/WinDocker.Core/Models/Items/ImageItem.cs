using CommunityToolkit.Mvvm.ComponentModel;

namespace WinDocker.Core.Models;

/// <summary>A row of the image list; the instance stays the same while the image row exists (see <see cref="ContainerItem"/>).</summary>
public sealed partial class ImageItem : ObservableObject
{
    public ImageItem(ImageInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        Key = ImageKey.From(info);
        Info = info;
    }

    public ImageKey Key { get; }

    [ObservableProperty]
    public partial ImageInfo Info { get; private set; }

    /// <summary>Takes over <paramref name="info"/>. Nothing is raised when it equals the current value (records compare by value).</summary>
    public void Update(ImageInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        Info = info;
    }
}
