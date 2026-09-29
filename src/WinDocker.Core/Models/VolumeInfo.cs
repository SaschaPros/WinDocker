using System.Globalization;

namespace WinDocker.Core.Models;

public sealed record VolumeInfo(string Name, string Driver, string Mountpoint, DateTimeOffset? CreatedAt)
{
    public string CreatedText => CreatedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? string.Empty;
}
