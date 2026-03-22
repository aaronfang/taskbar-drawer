using System.IO;
using System.Text.Json;

namespace TaskbarDrawer;

public enum DisplayMode
{
    IconAndName = 0,  // 图标+名称
    IconOnly = 1,     // 只显示图标
    NameOnly = 2      // 只显示名称
}

public class AppSettings
{
    public double IconSize { get; set; } = 36;
    public bool IsDarkMode { get; set; } = false;
    public double WindowWidth { get; set; } = 360;
    public double WindowHeight { get; set; } = 320;
    public DisplayMode DisplayMode { get; set; } = DisplayMode.IconAndName;
    public Dictionary<string, int> CustomOrder { get; set; } = new Dictionary<string, int>();
    public Dictionary<string, bool> FolderExpandStates { get; set; } = new Dictionary<string, bool>();
    
    // 背景模糊设置
    public double BackgroundOpacity { get; set; } = 0.75;  // 透明度 0-1 (默认75%)
    public bool EnableBlur { get; set; } = true;  // 是否启用模糊效果
}

public static class SettingsManager
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TaskbarDrawer",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }
}
