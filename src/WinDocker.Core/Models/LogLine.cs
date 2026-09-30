using System.Globalization;

namespace WinDocker.Core.Models;

public sealed record LogLine(DateTimeOffset? Timestamp, string Text, bool IsError)
{
    public string TimestampText =>
        Timestamp?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) ?? string.Empty;
}
