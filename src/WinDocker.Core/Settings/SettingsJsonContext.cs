using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinDocker.Core.Settings;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
