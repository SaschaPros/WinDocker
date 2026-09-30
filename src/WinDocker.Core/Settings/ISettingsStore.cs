namespace WinDocker.Core.Settings;

public interface ISettingsStore
{
    /// <summary>Reads the stored settings; anything that is missing or unreadable yields the defaults.</summary>
    AppSettings Load();

    void Save(AppSettings settings);
}
