using System.Text.Json;

namespace WinDocker.Core.Settings;

/// <summary>Keeps the settings in a JSON file.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string path;

    public JsonSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        this.path = path;
    }

    /// <summary><c>%LOCALAPPDATA%\WinDocker\settings.json</c>.</summary>
    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinDocker", "settings.json");

    /// <summary>Returns the defaults when the file is missing, cannot be read or does not contain valid settings.</summary>
    public AppSettings Load()
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Creates the directory when needed and replaces the file in one step, so a crash cannot leave half a file behind.</summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var temporaryPath = fullPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));
        File.Move(temporaryPath, fullPath, overwrite: true);
    }
}
