using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace TaskbarDrawer;

public partial class MainWindow : Window
{
    private readonly string _shortcutsFolder;
    private AppSettings _settings;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private DateTime _lastDeactivated = DateTime.MinValue;
    private bool _isRealClose = false;
    
    // 拖拽相关字段
    private System.Windows.Point _dragStartPoint;
    private ShortcutItem? _draggedItem;
    private string? _draggedCategory;
    private bool _isDragging = false;

    public double IconSize
    {
        get { return (double)GetValue(IconSizeProperty); }
        set { SetValue(IconSizeProperty, value); }
    }
    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register("IconSize", typeof(double), typeof(MainWindow), new PropertyMetadata(36.0, OnSizeChanged));

    public double ButtonWidth
    {
        get { return (double)GetValue(ButtonWidthProperty); }
        set { SetValue(ButtonWidthProperty, value); }
    }
    public static readonly DependencyProperty ButtonWidthProperty = DependencyProperty.Register("ButtonWidth", typeof(double), typeof(MainWindow), new PropertyMetadata(75.0));

    public double ButtonHeight
    {
        get { return (double)GetValue(ButtonHeightProperty); }
        set { SetValue(ButtonHeightProperty, value); }
    }
    public static readonly DependencyProperty ButtonHeightProperty = DependencyProperty.Register("ButtonHeight", typeof(double), typeof(MainWindow), new PropertyMetadata(85.0));

    public DisplayMode CurrentDisplayMode
    {
        get { return (DisplayMode)GetValue(CurrentDisplayModeProperty); }
        set { SetValue(CurrentDisplayModeProperty, value); }
    }
    public static readonly DependencyProperty CurrentDisplayModeProperty = DependencyProperty.Register("CurrentDisplayMode", typeof(DisplayMode), typeof(MainWindow), new PropertyMetadata(DisplayMode.IconAndName, OnDisplayModeChanged));

    private static void OnDisplayModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MainWindow w)
        {
            w.UpdateButtonMetrics();
        }
    }

    private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MainWindow w)
        {
            w.UpdateButtonMetrics();
        }
    }

    private void UpdateButtonMetrics()
    {
        switch (CurrentDisplayMode)
        {
            case DisplayMode.IconOnly:
                ButtonWidth = IconSize + 15;
                ButtonHeight = IconSize + 15;
                break;
            case DisplayMode.NameOnly:
                ButtonWidth = 100;
                ButtonHeight = 36;
                break;
            case DisplayMode.IconAndName:
            default:
                ButtonWidth = IconSize + 39;
                ButtonHeight = IconSize + 49;
                break;
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        
        _settings = SettingsManager.Load();
        Width = _settings.WindowWidth;
        Height = _settings.WindowHeight;
        IconSize = _settings.IconSize;
        CurrentDisplayMode = _settings.DisplayMode;
        DarkModeCheck.IsChecked = _settings.IsDarkMode;
        UpdateDisplayModeRadioButtons();
        UpdateButtonMetrics();
        ApplyTheme();

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _shortcutsFolder = Path.Combine(appData, "TaskbarDrawer", "Shortcuts");
        
        if (!Directory.Exists(_shortcutsFolder))
        {
            Directory.CreateDirectory(_shortcutsFolder);
        }

        SetupNotifyIcon();
    }

    private void SetupNotifyIcon()
    {
        System.Drawing.Icon? appIcon = null;
        try
        {
            // Try to use the application's icon
            var processModule = Process.GetCurrentProcess().MainModule;
            if (processModule?.FileName != null)
            {
                appIcon = System.Drawing.Icon.ExtractAssociatedIcon(processModule.FileName);
            }
        }
        catch { }

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = appIcon ?? System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "AppDrawer"
        };
        
        _notifyIcon.Click += (s, e) => ToggleVisibility();
        
        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        var showItem = new System.Windows.Forms.ToolStripMenuItem("显示抽屉 (Show)");
        showItem.Click += (s, e) => ToggleVisibility();
        var exitItem = new System.Windows.Forms.ToolStripMenuItem("完全退出 (Exit)");
        exitItem.Click += (s, e) => 
        {
            _isRealClose = true;
            System.Windows.Application.Current.Shutdown();
        };
        
        contextMenu.Items.Add(showItem);
        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        contextMenu.Items.Add(exitItem);
        
        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    private void ApplyTheme()
    {
        var dict = new ResourceDictionary { Source = new Uri($"Themes/{(_settings.IsDarkMode ? "Dark" : "Light")}.xaml", UriKind.Relative) };
        System.Windows.Application.Current.Resources.MergedDictionaries.Clear();
        System.Windows.Application.Current.Resources.MergedDictionaries.Add(dict);
    }

    private void Theme_Changed(object sender, RoutedEventArgs e)
    {
        if (DarkModeCheck.IsChecked.HasValue && _settings != null)
        {
            _settings.IsDarkMode = DarkModeCheck.IsChecked.Value;
            ApplyTheme();
            SaveSettings();
        }
    }

    private void DisplayMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_settings == null || !(sender is System.Windows.Controls.RadioButton radio)) return;

        if (radio.Name == "IconAndNameRadio")
            CurrentDisplayMode = DisplayMode.IconAndName;
        else if (radio.Name == "IconOnlyRadio")
            CurrentDisplayMode = DisplayMode.IconOnly;
        else if (radio.Name == "NameOnlyRadio")
            CurrentDisplayMode = DisplayMode.NameOnly;

        _settings.DisplayMode = CurrentDisplayMode;
        SaveSettings();
    }

    private void UpdateDisplayModeRadioButtons()
    {
        IconAndNameRadio.IsChecked = CurrentDisplayMode == DisplayMode.IconAndName;
        IconOnlyRadio.IsChecked = CurrentDisplayMode == DisplayMode.IconOnly;
        NameOnlyRadio.IsChecked = CurrentDisplayMode == DisplayMode.NameOnly;
    }

    private void IconSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_settings != null)
        {
            _settings.IconSize = IconSize;
            SaveSettings();
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PositionWindow();
        LoadShortcuts();
    }

    public void ToggleVisibility()
    {
        if ((DateTime.Now - _lastDeactivated).TotalMilliseconds < 300)
        {
            return;
        }

        if (Visibility == Visibility.Visible && IsActive)
        {
            HideWindow();
        }
        else
        {
            LoadShortcuts(); 
            PositionWindow();
            ShowWindow();
        }
    }

    private void HideWindow()
    {
        Hide();
        
        // Reset view state to Main on hide
        SettingsView.Visibility = Visibility.Collapsed;
        MainView.Visibility = Visibility.Visible;
    }

    private void ShowWindow()
    {
        Show();
        Activate();
    }

    private void LoadShortcuts()
    {
        var categories = ShortcutHelper.GetCategoriesFromFolder(_shortcutsFolder, _settings.CustomOrder, _settings.FolderExpandStates);
        ShortcutList.ItemsSource = categories;
    }

    private void PositionWindow()
    {
        var workArea = SystemParameters.WorkArea;
        var mousePos = System.Windows.Forms.Cursor.Position;
        
        double targetLeft = mousePos.X - (Width / 2);
        
        if (targetLeft < workArea.Left) targetLeft = workArea.Left;
        if (targetLeft + Width > workArea.Right) targetLeft = workArea.Right - Width;

        Left = targetLeft;
        Top = workArea.Bottom - Height;
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        _lastDeactivated = DateTime.Now;
        HideWindow();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (!_isRealClose)
        {
            e.Cancel = true;
            HideWindow();
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start("explorer.exe", _shortcutsFolder);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("无法打开文件夹: " + ex.Message);
        }
        HideWindow();
    }

    private void ExitApp_Click(object sender, RoutedEventArgs e)
    {
        _isRealClose = true;
        System.Windows.Application.Current.Shutdown();
    }

    private void ToggleSettings_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsView.Visibility == Visibility.Visible)
        {
            SettingsView.Visibility = Visibility.Collapsed;
            MainView.Visibility = Visibility.Visible;
        }
        else
        {
            MainView.Visibility = Visibility.Collapsed;
            SettingsView.Visibility = Visibility.Visible;
        }
    }

    private void Shortcut_Click(object sender, RoutedEventArgs e)
    {
        // 如果正在拖拽，不处理点击
        if (_isDragging)
        {
            return;
        }
        
        if (sender is System.Windows.Controls.Button btn && btn.DataContext is ShortcutItem item)
        {
            if (item.IsFolder && item.SubItems != null && item.SubItems.Count > 0)
            {
                // Toggle folder expand/collapse
                item.IsExpanded = !item.IsExpanded;
                LoadShortcuts(); // Refresh to show/hide sub-items
            }
            else
            {
                // Launch shortcut directly
                LaunchShortcut(item);
            }
        }
    }

    private void SubMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // No longer needed but keep for compatibility
    }

    private void LaunchShortcut(ShortcutItem item)
    {
        try
        {
            var pInfo = new ProcessStartInfo
            {
                FileName = item.FilePath,
                UseShellExecute = true
            };
            Process.Start(pInfo);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("无法启动程序: " + ex.Message);
        }
        HideWindow();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_settings != null && IsLoaded)
        {
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
            SaveSettings();
        }
    }

    private void SaveSettings()
    {
        SettingsManager.Save(_settings);
    }

    // 拖拽事件处理方法
    private void Icon_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.DataContext is ShortcutItem item && !item.IsFolder)
        {
            _dragStartPoint = e.GetPosition(null);
            _draggedItem = item;
            _isDragging = false;
            
            // 找到所属的分组
            if (ShortcutList.ItemsSource is System.Collections.IEnumerable categories)
            {
                foreach (ShortcutCategory category in categories)
                {
                    if (category.Shortcuts.Contains(item))
                    {
                        _draggedCategory = category.FolderPath ?? "root";
                        break;
                    }
                }
            }
        }
    }

    private void Icon_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _draggedItem != null && !_isDragging)
        {
            System.Windows.Point currentPosition = e.GetPosition(null);
            System.Windows.Vector diff = _dragStartPoint - currentPosition;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (sender is System.Windows.Controls.Button button)
                {
                    _isDragging = true;
                    System.Windows.DataObject dragData = new System.Windows.DataObject("ShortcutItem", _draggedItem);
                    DragDrop.DoDragDrop(button, dragData, System.Windows.DragDropEffects.Move);
                    _isDragging = false;
                }
            }
        }
    }

    private void Icon_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent("ShortcutItem"))
        {
            e.Effects = System.Windows.DragDropEffects.Move;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Icon_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent("ShortcutItem") && sender is System.Windows.Controls.Button button && button.DataContext is ShortcutItem targetItem)
        {
            var draggedItem = e.Data.GetData("ShortcutItem") as ShortcutItem;
            if (draggedItem != null && draggedItem != targetItem && !targetItem.IsFolder)
            {
                // 找到目标所属的分组
                string? targetCategory = null;
                if (ShortcutList.ItemsSource is System.Collections.IEnumerable categories)
                {
                    foreach (ShortcutCategory category in categories)
                    {
                        if (category.Shortcuts.Contains(targetItem))
                        {
                            targetCategory = category.FolderPath ?? "root";
                            break;
                        }
                    }
                }

                // 只允许在同一分组内拖拽
                if (targetCategory == _draggedCategory)
                {
                    ReorderShortcuts(draggedItem, targetItem, _draggedCategory!);
                }
            }
        }
        
        _draggedItem = null;
        _draggedCategory = null;
    }

    private void ReorderShortcuts(ShortcutItem draggedItem, ShortcutItem targetItem, string categoryKey)
    {
        if (ShortcutList.ItemsSource is System.Collections.IEnumerable categories)
        {
            foreach (ShortcutCategory category in categories)
            {
                var matchKey = category.FolderPath ?? "root";
                if (matchKey == categoryKey)
                {
                    var shortcuts = category.Shortcuts;
                    int draggedIndex = shortcuts.IndexOf(draggedItem);
                    int targetIndex = shortcuts.IndexOf(targetItem);

                    if (draggedIndex >= 0 && targetIndex >= 0)
                    {
                        // 移动项目
                        shortcuts.RemoveAt(draggedIndex);
                        if (draggedIndex < targetIndex)
                            targetIndex--;
                        shortcuts.Insert(targetIndex, draggedItem);

                        // 保存新顺序
                        SaveCustomOrder(categoryKey, shortcuts);
                        
                        // 刷新显示
                        LoadShortcuts();
                    }
                    break;
                }
            }
        }
    }

    private void SaveCustomOrder(string categoryKey, List<ShortcutItem> shortcuts)
    {
        // 清除该分组的旧排序
        var keysToRemove = _settings.CustomOrder.Keys
            .Where(k => k.StartsWith(categoryKey == "root" ? "root:" : $"folder:{categoryKey}:"))
            .ToList();
        foreach (var key in keysToRemove)
        {
            _settings.CustomOrder.Remove(key);
        }

        // 保存新排序
        for (int i = 0; i < shortcuts.Count; i++)
        {
            string key = categoryKey == "root" 
                ? $"root:{shortcuts[i].FilePath}" 
                : $"folder:{categoryKey}:{shortcuts[i].FilePath}";
            _settings.CustomOrder[key] = i;
        }

        SaveSettings();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        base.OnClosed(e);
    }
}