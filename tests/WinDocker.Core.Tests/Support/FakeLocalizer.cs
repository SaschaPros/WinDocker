using System.Globalization;
using WinDocker.Core.Services;

namespace WinDocker.Core.Tests.Support;

/// <summary>Returns the key, followed by the arguments when there are any: <c>Key|arg0|arg1</c>.</summary>
internal sealed class FakeLocalizer : ILocalizer
{
    public string GetString(string key) => key;

    public string Format(string key, params object[] args) =>
        args.Length == 0
            ? key
            : $"{key}|{string.Join("|", args.Select(arg => Convert.ToString(arg, CultureInfo.InvariantCulture)))}";
}
