using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FloatTodo.Models;

namespace FloatTodo.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppSettings Value { get; }

    public SettingsStore()
    {
        AppPaths.EnsureDirectory();
        try
        {
            Value = File.Exists(AppPaths.Settings)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.Settings), JsonOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch (JsonException)
        {
            // 配置损坏时回退默认值，待办 SQLite 数据不会受影响。
            Value = new AppSettings();
        }
        Value.DockThresholdPx = Math.Clamp(Value.DockThresholdPx, 8, 120);
        Value.VisiblePercent = Math.Clamp(Value.VisiblePercent, 20, 80);
        Value.CollapseDelayMs = Math.Clamp(Value.CollapseDelayMs, 100, 2000);
        Value.WaterIntervalMinutes = Math.Clamp(Value.WaterIntervalMinutes, 10, 240);
        Value.SittingIntervalMinutes = Math.Clamp(Value.SittingIntervalMinutes, 10, 240);
        Value.QuietStartHour = Math.Clamp(Value.QuietStartHour, 0, 23);
        Value.QuietEndHour = Math.Clamp(Value.QuietEndHour, 0, 23);
    }

    public void Save()
    {
        AppPaths.EnsureDirectory();
        string temporary = AppPaths.Settings + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Value, JsonOptions));
        File.Move(temporary, AppPaths.Settings, overwrite: true);
    }
}
