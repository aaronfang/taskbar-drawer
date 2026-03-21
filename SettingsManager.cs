using System;
using System.IO;
using System.Text.Json;

namespace TaskbarDrawer;

public enum DisplayMode
{
    IconAndName = 0,  // 默认：图标+名称
    IconOnly = 1,     // 只显示图标
    NameOnly = 2      // 只显示名称
}

public class AppSettings
{
    public double WindowWidth { get; set; } = 360;
    public double WindowHeight { get; set; } = 320;
    public double IconSize { get; set; } = 36;
    public bool IsDarkMode { get; set; } = false;
    public DisplayMode DisplayMode { get; set; } = DisplayMode.IconAndName;
}

public static class SettingsManager
{
    private static readonly string SettingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarDrawer", "settings.json");

    public static AppSettings Load()
    {
        if (File.Exists(SettingsFile))
        {
            try
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile)) ?? new AppSettings();
            }
            catch { }
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings));
        }
        catch { }
    }
}