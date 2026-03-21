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

    private static Dictionary<string, ShortcutItem> _folderStates = new Dictionary<string, ShortcutItem>();

    public static List<ShortcutCategory> GetCategoriesFromFolder(string rootPath)
    {
        var categories = new List<ShortcutCategory>();
        if (!Directory.Exists(rootPath)) return categories;

        var allItems = new List<ShortcutItem>();

        // 1. Root files - direct shortcuts
        var rootFiles = Directory.GetFiles(rootPath).Where(IsShortcut).ToArray();
        allItems.AddRange(GetShortcuts(rootFiles));

        // 2. Subdirectories - show as folder buttons
        foreach (var dir in Directory.GetDirectories(rootPath))
        {
            var files = Directory.GetFiles(dir).Where(IsShortcut).ToArray();
            if (files.Length > 0)
            {
                // Preserve folder expanded state
                ShortcutItem? existingFolder;
                _folderStates.TryGetValue(dir, out existingFolder);
                
                var folderItem = new ShortcutItem
                {
                    Name = "📁 " + Path.GetFileName(dir),
                    FilePath = dir,
                    TargetPath = dir,
                    IsFolder = true,
                    SubItems = GetShortcuts(files),
                    Icon = null,
                    IsExpanded = existingFolder?.IsExpanded ?? false
                };
                
                _folderStates[dir] = folderItem;
                allItems.Add(folderItem);
                
                // If expanded, add sub-items right after the folder
                if (folderItem.IsExpanded && folderItem.SubItems != null)
                {
                    allItems.AddRange(folderItem.SubItems);
                }
            }
        }

        if (allItems.Count > 0)
        {
            categories.Add(new ShortcutCategory { Name = "所有项目 (All Items)", Shortcuts = allItems });
        }

        return categories;
    }



    private static List<ShortcutItem> GetShortcuts(string[] files)
    {
        var result = new List<ShortcutItem>();
        foreach (var file in files)
        {
            try
            {
                System.Windows.Media.ImageSource? iconSource = null;

                SHFILEINFO shinfo = new SHFILEINFO();
                IntPtr hImg = SHGetFileInfo(file, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON);

                if (shinfo.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        iconSource = Imaging.CreateBitmapSourceFromHIcon(
                            shinfo.hIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        
                        if (iconSource.CanFreeze)
                            iconSource.Freeze();
                    }
                    finally
                    {
                        DestroyIcon(shinfo.hIcon);
                    }
                }

                result.Add(new ShortcutItem
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    FilePath = file,
                    TargetPath = file,
                    Icon = iconSource
                });
            }
            catch { }
        }
        return result;
    }
}