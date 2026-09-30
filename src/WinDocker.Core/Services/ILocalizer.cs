namespace WinDocker.Core.Services;

/// <summary>Looks up user-visible texts by resource key. Keys are defined in <see cref="Localization.ResourceKeys"/>.</summary>
public interface ILocalizer
{
    string GetString(string key);

    /// <summary>Looks up the text for <paramref name="key"/> and substitutes the composite format placeholders.</summary>
    string Format(string key, params object[] args);
}
