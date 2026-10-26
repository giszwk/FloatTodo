using System;
using System.IO;
using System.Windows;
using FloatTodo.Services;
using Microsoft.Win32;
using FloatTodo.Models;

namespace FloatTodo;

public partial class SettingsWindow : Window
{
    private string? _pendingAtlasPath;
    private bool _removeAtlas;
    public bool AtlasChanged { get; private set; }
    public SkinTheme SelectedSkin { get; private set; }
    public int ThresholdPx { get; private set; }
    public int VisiblePercent { get; private set; }
    public int CollapseDelayMs { get; private set; }
    public bool WaterEnabled { get; private set; }
    public int WaterMinutes { get; private set; }
    public bool SittingEnabled { get; private set; }
    public int SittingMinutes { get; private set; }
    public bool SoundEnabled { get; private set; }
    public bool SuppressFullscreen { get; private set; }
    public int QuietStartHour { get; private set; }
    public int QuietEndHour { get; private set; }

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        SkinCombo.SelectedIndex = settings.Skin == SkinTheme.BlackCat ? 1 : 0;
        RefreshAtlasStatus();
        DistanceBox.Text = settings.DockThresholdPx.ToString();
        VisibleBox.Text = settings.VisiblePercent.ToString();
        DelayBox.Text = settings.CollapseDelayMs.ToString();
        WaterCheck.IsChecked = settings.WaterEnabled;
        WaterBox.Text = settings.WaterIntervalMinutes.ToString();
        SittingCheck.IsChecked = settings.SittingEnabled;
        SittingBox.Text = settings.SittingIntervalMinutes.ToString();
        SoundCheck.IsChecked = settings.ReminderSound;
        FullscreenCheck.IsChecked = settings.SuppressFullscreen;
        QuietStartBox.Text = settings.QuietStartHour.ToString();
        QuietEndBox.Text = settings.QuietEndHour.ToString();
    }

    private void RefreshAtlasStatus()
    {
        AtlasStatus.Text = _pendingAtlasPath is not null
            ? "保存后导入：" + _pendingAtlasPath
            : _removeAtlas ? "保存后移除自定义图集，恢复内置小黑猫。"
            : File.Exists(SkinAtlas.StoragePath) ? "已安装用户图集（切换小黑猫皮肤后生效）" : "当前使用内置原创小黑猫绘图。";
    }

    private void ImportAtlas_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "选择你有权使用的 PNG 精灵图",
            Filter = "PNG 精灵图 (*.png)|*.png",
            CheckFileExists = true
        };
        if (picker.ShowDialog(this) != true) return;
        if (!SkinAtlas.TryOpen(picker.FileName, out _, out var error))
        {
            MessageBox.Show(this, error ?? "图集格式有误。", "无法导入", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _pendingAtlasPath = picker.FileName;
        _removeAtlas = false;
        SkinCombo.SelectedIndex = 1;
        RefreshAtlasStatus();
    }

    private void RemoveAtlas_Click(object sender, RoutedEventArgs e)
    {
        _removeAtlas = true;
        _pendingAtlasPath = null;
        RefreshAtlasStatus();
    }

    private bool SaveAtlasChanges()
    {
        if (!_removeAtlas && _pendingAtlasPath is null) return true;
        try
        {
            string target = SkinAtlas.StoragePath;
            if (_removeAtlas)
            {
                if (File.Exists(target)) File.Delete(target);
                AtlasChanged = true;
                return true;
            }
            if (_pendingAtlasPath is null) return true;
            if (!SkinAtlas.TryOpen(_pendingAtlasPath, out _, out var error))
            {
                MessageBox.Show(this, error ?? "图集无效。", "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!string.Equals(Path.GetFullPath(_pendingAtlasPath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                string temporary = target + ".tmp";
                File.Copy(_pendingAtlasPath, temporary, true);
                File.Move(temporary, target, true);
            }
            AtlasChanged = true;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "无法保存图集：" + ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(DistanceBox.Text, out int threshold) || threshold is < 8 or > 120 ||
            !int.TryParse(VisibleBox.Text, out int visible) || visible is < 20 or > 80 ||
            !int.TryParse(DelayBox.Text, out int delay) || delay is < 100 or > 2000 ||
            !int.TryParse(WaterBox.Text, out int water) || water is < 10 or > 240 ||
            !int.TryParse(SittingBox.Text, out int sitting) || sitting is < 10 or > 240 ||
            !int.TryParse(QuietStartBox.Text, out int quietStart) || quietStart is < 0 or > 23 ||
            !int.TryParse(QuietEndBox.Text, out int quietEnd) || quietEnd is < 0 or > 23)
        {
            MessageBox.Show(this, "请按各字段标注的范围填写整数。", "输入有误", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!SaveAtlasChanges()) return;
        SelectedSkin = SkinCombo.SelectedIndex == 1 ? SkinTheme.BlackCat : SkinTheme.DefaultBall;
        ThresholdPx = threshold;
        VisiblePercent = visible;
        CollapseDelayMs = delay;
        WaterEnabled = WaterCheck.IsChecked == true;
        WaterMinutes = water;
        SittingEnabled = SittingCheck.IsChecked == true;
        SittingMinutes = sitting;
        SoundEnabled = SoundCheck.IsChecked == true;
        SuppressFullscreen = FullscreenCheck.IsChecked == true;
        QuietStartHour = quietStart;
        QuietEndHour = quietEnd;
        DialogResult = true;
    }
}
