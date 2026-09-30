using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinDocker.Core.Settings;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(ListLayoutSettings))]
[JsonSerializable(typeof(ColumnSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
