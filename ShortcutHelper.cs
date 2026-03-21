using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace TaskbarDrawer;

public class ShortcutItem : System.ComponentModel.INotifyPropertyChanged
{
    public required string Name { get; set; }
    public required string FilePath { get; set; }
    public required string TargetPath { get; set; }
    public System.Windows.Media.ImageSource? Icon { get; set; }
    public bool IsFolder { get; set; }
    public List<ShortcutItem>? SubItems { get; set; }
    
    private bool _isExpanded;
    public bool IsExpanded 
    { 
        get => _isExpanded; 
        set 
        { 
            _isExpanded = value; 
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsExpanded)));
        } 
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public class ShortcutCategory
{
    public required string Name { get; set; }
    public required List<ShortcutItem> Shortcuts { get; set; }
    public bool IsFolder { get; set; }
    
    private bool _isExpanded = true;
    public bool IsExpanded 
    { 
        get => _isExpanded; 
        set => _isExpanded = value;
    }
    
    public string? FolderPath { get; set; }
}

public static class ShortcutHelper
{
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;

    private static bool IsShortcut(string f) => f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".url", StringComparison.OrdinalIgnoreCase);

    public static List<ShortcutCategory> GetCategoriesFromFolder(string rootPath, Dictionary<string, int>? customOrder = null, Dictionary<string, bool>? folderExpandStates = null)
    {
        var categories = new List<ShortcutCategory>();
        if (!Directory.Exists(rootPath)) return categories;

        // 1. Root files - direct shortcuts (not in subfolders)
        var rootFiles = Directory.GetFiles(rootPath).Where(IsShortcut).ToArray();
        var rootItems = GetShortcuts(rootFiles);
        
        // Apply custom order to root items
        if (customOrder != null && customOrder.Count > 0)
        {
            rootItems = rootItems.OrderBy(item => 
            {
                var key = "root:" + item.FilePath;
                return customOrder.ContainsKey(key) ? customOrder[key] : int.MaxValue;
            }).ToList();
        }
        
        if (rootItems.Count > 0)
        {
            categories.Add(new ShortcutCategory 
            { 
                Name = "快捷方式", 
                Shortcuts = rootItems,
                IsFolder = false
            });
        }

        // 2. Subdirectories - show as folder groups
        var subDirs = Directory.GetDirectories(rootPath);
        foreach (var dir in subDirs)
        {
            var files = Directory.GetFiles(dir).Where(IsShortcut).ToArray();
            if (files.Length > 0)
            {
                var folderName = Path.GetFileName(dir);
                var isExpanded = folderExpandStates?.ContainsKey(dir) == true 
                    ? folderExpandStates[dir] 
                    : true;
                
                var folderItems = GetShortcuts(files);
                
                // Apply custom order to folder items
                if (customOrder != null && customOrder.Count > 0)
                {
                    folderItems = folderItems.OrderBy(item => 
                    {
                        var key = "folder:" + dir + ":" + item.FilePath;
                        return customOrder.ContainsKey(key) ? customOrder[key] : int.MaxValue;
                    }).ToList();
                }
                
                categories.Add(new ShortcutCategory 
                { 
                    Name = folderName,
                    Shortcuts = folderItems,
                    IsFolder = true,
                    IsExpanded = isExpanded,
                    FolderPath = dir
                });
            }
        }

        return categories;
    }



    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private static System.Windows.Media.ImageSource? IconToImageSourceFromHIcon(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero) return null;
        
        try
        {
            // 创建 BitmapSource
            var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            
            // 创建一个独立的副本,避免依赖原始句柄
            var writeableBitmap = new WriteableBitmap(bitmapSource);
            writeableBitmap.Freeze();
            
            return writeableBitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error converting icon: {ex.Message}");
        }
        
        return null;
    }

    private static System.Windows.Media.ImageSource? GetDefaultIcon()
    {
        try
        {
            var systemIcon = System.Drawing.SystemIcons.Application;
            if (systemIcon != null)
            {
                return IconToImageSourceFromHIcon(systemIcon.Handle);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error creating default icon: {ex.Message}");
        }
        return null;
    }

    private static System.Windows.Media.ImageSource? ExtractIconSafe(string filePath)
    {
        // 方法1：使用 SHGetFileInfo - 这是最可靠的方法，直接从 shell 获取图标
        try
        {
            SHFILEINFO shinfo = new SHFILEINFO();
            IntPtr hImg = SHGetFileInfo(filePath, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON);

            if (hImg != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
            {
                try
                {
                    var imageSource = IconToImageSourceFromHIcon(shinfo.hIcon);
                    if (imageSource != null)
                    {
                        return imageSource;
                    }
                }
                finally
                {
                    DestroyIcon(shinfo.hIcon);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SHGetFileInfo failed for {filePath}: {ex.Message}");
        }

        // 方法2：使用 ExtractAssociatedIcon
        try
        {
            var icon = Icon.ExtractAssociatedIcon(filePath);
            if (icon != null)
            {
                try
                {
                    var imageSource = IconToImageSourceFromHIcon(icon.Handle);
                    if (imageSource != null)
                    {
                        return imageSource;
                    }
                }
                finally
                {
                    icon.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ExtractAssociatedIcon failed for {filePath}: {ex.Message}");
        }

        // 方法3：返回默认图标
        System.Diagnostics.Debug.WriteLine($"Using default icon for {filePath}");
        return GetDefaultIcon();
    }

    private static List<ShortcutItem> GetShortcuts(string[] files)
    {
        var result = new List<ShortcutItem>();
        foreach (var file in files)
        {
            try
            {
                var iconSource = ExtractIconSafe(file);
                
                // 确保图标不为 null
                if (iconSource == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Warning: Failed to extract icon for {file}");
                    iconSource = GetDefaultIcon();
                }

                result.Add(new ShortcutItem
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    FilePath = file,
                    TargetPath = file,
                    Icon = iconSource
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading shortcut {file}: {ex.Message}");
            }
        }
        return result;
    }
}
