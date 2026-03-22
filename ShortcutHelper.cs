using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
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

    // COM 接口用于解析 .lnk 快捷方式
    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig]
        int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    private static bool IsShortcut(string f) => f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".url", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 解析 .lnk 快捷方式文件,获取目标路径
    /// </summary>
    private static string? GetShortcutTarget(string shortcutPath)
    {
        if (!shortcutPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return shortcutPath;

        try
        {
            var link = (IShellLinkW)new ShellLink();
            ((IPersistFile)link).Load(shortcutPath, 0);
            
            var sb = new StringBuilder(260);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            
            var target = sb.ToString();
            return string.IsNullOrEmpty(target) ? null : target;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to resolve shortcut {shortcutPath}: {ex.Message}");
            return null;
        }
    }

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
        // 对于 .lnk 文件,先尝试获取目标路径
        string? targetPath = null;
        if (filePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            targetPath = GetShortcutTarget(filePath);
            if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
            {
                System.Diagnostics.Debug.WriteLine($"Resolved shortcut: {filePath} -> {targetPath}");
                // 优先从目标程序提取图标
                var targetIcon = TryExtractIcon(targetPath);
                if (targetIcon != null)
                    return targetIcon;
            }
        }

        // 方法1：使用 SHGetFileInfo - 从快捷方式本身获取图标
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

    /// <summary>
    /// 尝试从文件提取图标(用于目标程序)
    /// </summary>
    private static System.Windows.Media.ImageSource? TryExtractIcon(string filePath)
    {
        try
        {
            // 方法1：ExtractAssociatedIcon
            var icon = Icon.ExtractAssociatedIcon(filePath);
            if (icon != null)
            {
                try
                {
                    return IconToImageSourceFromHIcon(icon.Handle);
                }
                finally
                {
                    icon.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TryExtractIcon failed for {filePath}: {ex.Message}");
        }

        // 方法2：SHGetFileInfo
        try
        {
            SHFILEINFO shinfo = new SHFILEINFO();
            IntPtr hImg = SHGetFileInfo(filePath, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON);

            if (hImg != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
            {
                try
                {
                    return IconToImageSourceFromHIcon(shinfo.hIcon);
                }
                finally
                {
                    DestroyIcon(shinfo.hIcon);
                }
            }
        }
        catch { }

        return null;
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

                // 获取真实目标路径(用于启动程序)
                var targetPath = file;
                if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    var resolvedTarget = GetShortcutTarget(file);
                    if (!string.IsNullOrEmpty(resolvedTarget))
                    {
                        targetPath = resolvedTarget;
                    }
                }

                result.Add(new ShortcutItem
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    FilePath = file,
                    TargetPath = targetPath,
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
