using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;
using WinDocker.Core.Services;

namespace WinDocker.Services;

/// <summary>Looks up texts in the app's resource file (<c>Strings/en-US/Resources.resw</c>).</summary>
internal sealed class ResourceLocalizer : ILocalizer
{
    private readonly ResourceLoader loader = new();

    public string GetString(string key) => loader.GetString(key);

    public string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, GetString(key), args);
}
