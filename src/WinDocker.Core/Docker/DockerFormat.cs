using System.Globalization;
using System.Text.Json;

namespace WinDocker.Core.Docker;

/// <summary>Formats Docker data the way the docker CLI does. All output is culture invariant.</summary>
public static class DockerFormat
{
    /// <summary>Placeholder the engine and the docker CLI use for a missing repository or tag.</summary>
    public const string NoneMarker = "<none>";

    private const string DigestPrefix = "sha256:";
    private const int ShortIdLength = 12;

    private static readonly string[] SizeUnits = ["B", "kB", "MB", "GB", "TB", "PB", "EB"];

    /// <summary>Strips the "sha256:" prefix and keeps the first 12 characters.</summary>
    public static string ShortId(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return string.Empty;
        }

        var span = id.AsSpan();
        if (span.StartsWith(DigestPrefix, StringComparison.Ordinal))
        {
            span = span[DigestPrefix.Length..];
        }

        return span.Length <= ShortIdLength ? span.ToString() : span[..ShortIdLength].ToString();
    }

    /// <summary>
    /// Returns the container name without the leading '/'. Names like "/db/alias" are link aliases
    /// created by another container and only used when the container has no name of its own.
    /// </summary>
    public static string ContainerName(IEnumerable<string>? names)
    {
        if (names is null)
        {
            return string.Empty;
        }

        string? first = null;
        foreach (var name in names)
        {
            first ??= name;
            if (name.AsSpan().TrimStart('/').IndexOf('/') < 0)
            {
                return name.TrimStart('/');
            }
        }

        return first?.TrimStart('/') ?? string.Empty;
    }

    /// <summary>
    /// Formats ports like <c>docker ps</c>: <c>0.0.0.0:8080-&gt;80/tcp</c> for published ports and
    /// <c>80/tcp</c> for exposed-only ports. Entries are ordered by container port and deduplicated.
    /// </summary>
    public static string Ports(IEnumerable<PortMapping>? ports)
    {
        if (ports is null)
        {
            return string.Empty;
        }

        var formatted = ports
            .OrderBy(port => port.PrivatePort)
            .ThenBy(port => string.IsNullOrEmpty(port.Ip))
            .ThenBy(port => port.Ip, StringComparer.Ordinal)
            .ThenBy(port => port.PublicPort)
            .ThenBy(port => port.Type, StringComparer.Ordinal)
            .Select(FormatPort)
            .Distinct(StringComparer.Ordinal);

        return string.Join(", ", formatted);
    }

    /// <summary>
    /// Formats a byte count with decimal units and three significant digits, for example
    /// <c>7.83MB</c> or <c>142MB</c>, like <c>docker images</c>.
    /// </summary>
    public static string Size(long bytes)
    {
        if (bytes < 1000)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{Math.Max(bytes, 0)}{SizeUnits[0]}");
        }

        // decimal keeps the division by powers of 1000 exact.
        var value = (decimal)bytes;
        var unit = 0;
        while (value >= 1000m && unit < SizeUnits.Length - 1)
        {
            value /= 1000m;
            unit++;
        }

        value = RoundToThreeSignificantDigits(value);
        if (value >= 1000m && unit < SizeUnits.Length - 1)
        {
            // 999.6 kB rounds to 1000 kB, which reads better as 1 MB.
            value /= 1000m;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{value:0.##}{SizeUnits[unit]}");
    }

    /// <summary>
    /// Extracts the "message" of a Docker API error response body. Falls back to the raw body
    /// when it is not the usual JSON error object, and to <see langword="null"/> when it is empty.
    /// </summary>
    public static string? DaemonMessage(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(message.GetString()))
            {
                return message.GetString()!.Trim();
            }
        }
        catch (JsonException)
        {
        }

        return responseBody.Trim();
    }

    private static string FormatPort(PortMapping port)
    {
        if (port.PublicPort is not > 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{port.PrivatePort}/{port.Type}");
        }

        var host = string.IsNullOrEmpty(port.Ip) ? string.Empty
            : port.Ip.Contains(':') ? $"[{port.Ip}]:"
            : $"{port.Ip}:";

        return string.Create(CultureInfo.InvariantCulture, $"{host}{port.PublicPort}->{port.PrivatePort}/{port.Type}");
    }

    private static decimal RoundToThreeSignificantDigits(decimal value)
    {
        var decimals = value < 10m ? 2 : value < 100m ? 1 : 0;
        return Math.Round(value, decimals, MidpointRounding.AwayFromZero);
    }
}
