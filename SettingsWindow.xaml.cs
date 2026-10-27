using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using FloatTodo.Services;
using FloatTodo.Views;
using Microsoft.Win32;
using FloatTodo.Models;

namespace FloatTodo;

public partial class SettingsWindow : Window
{
    private string? _pendingAtlasPath;
    private bool _removeAtlas;
    private string? _pendingFullGif;
    private string? _pendingPeekGif;
    private bool _removeGifs;
    public bool AtlasChanged { get; private set; }
    public event Action<SkinTheme>? SkinReloadRequested;
    public SkinTheme SelectedSkin { get; private set; }
    public int ThresholdPx { get; private set; }
    public int VisiblePercent { get; private set; }
    public int BallScalePercent { get; private set; }
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
        SkinCombo.SelectedIndex = (int)settings.Skin;
        RefreshAtlasStatus();
        RefreshGifStatus();
        RefreshCustomStatus();
        DistanceBox.Text = settings.DockThresholdPx.ToString();
        VisibleBox.Text = settings.VisiblePercent.ToString();
        SizeBox.Text = settings.BallScalePercent.ToString();
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
            : _removeAtlas ? "保存后移除自定义图集，恢复内置皮肤。"
            : File.Exists(SkinAtlas.StoragePath) ? "已安装用户 PNG 图集。" : "未导入 PNG 图集，使用内置 GIF 皮肤。";
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

    private void RefreshGifStatus()
    {
        GifStatus.Text = _removeGifs ? "保存后删除两个 GIF，恢复内置 GIF 动作。"
            : $"展开：{(_pendingFullGif is not null ? "待导入" : File.Exists(SkinGif.FullPath) ? "已导入" : "内置 GIF")}" +
              $"；扒边：{(_pendingPeekGif is not null ? "待导入" : File.Exists(SkinGif.PeekPath) ? "已导入" : "内置 GIF")}";
    }

    private void RefreshCustomStatus()
    {
        int portable = CountCustomFiles(SkinGif.PortableCustomDir);
        int user = CountCustomFiles(SkinGif.UserCustomDir);
        var active = CatSkinVisual.ResolveCustomSkinDir();
        string state = active == SkinGif.PortableCustomDir ? "当前使用：程序目录"
            : active == SkinGif.UserCustomDir ? "当前使用：用户目录（兼容回退）"
            : "当前回退：默认蓝色悬浮球";
        CustomStatus.Text =
            $"程序目录（优先）：{SkinGif.PortableCustomDir} — {portable}/4\n" +
            $"用户目录（兼容）：{SkinGif.UserCustomDir} — {user}/4\n" + state;
    }

    private static int CountCustomFiles(string dir)
    {
        int found = 0;
        foreach (var name in SkinGif.CustomFileNames)
            if (File.Exists(Path.Combine(dir, name))) found++;
        return found;
    }

    private void OpenCustomSkinFolder_Click(object sender, RoutedEventArgs e)
    {
        string dir = SkinGif.PortableCustomDir;
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 程序目录不可写（例如装在 Program Files）时退回用户目录。
            dir = SkinGif.UserCustomDir;
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception inner) when (inner is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, "无法创建自定义皮肤目录：" + inner.Message, "打开失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
            RefreshCustomStatus();
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, "无法打开文件夹：" + ex.Message, "打开失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ReloadSkin_Click(object sender, RoutedEventArgs e)
    {
        SkinReloadRequested?.Invoke((SkinTheme)SkinCombo.SelectedIndex);
        RefreshCustomStatus();
    }

    private void ImportFullGif_Click(object sender, RoutedEventArgs e) => ImportGif(false);
    private void ImportPeekGif_Click(object sender, RoutedEventArgs e) => ImportGif(true);

    private void ImportGif(bool peek)
    {
        var picker = new OpenFileDialog
        {
            Title = peek ? "选择扒边时播放的 GIF" : "选择展开时播放的 GIF",
            Filter = "GIF 动画 (*.gif)|*.gif", CheckFileExists = true
        };
        if (picker.ShowDialog(this) != true) return;
        if (!SkinGif.TryOpen(picker.FileName, out _, out var error))
        {
            MessageBox.Show(this, error ?? "GIF 无效。", "无法导入", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (peek) _pendingPeekGif = picker.FileName;
        else _pendingFullGif = picker.FileName;
        _removeGifs = false;
        SkinCombo.SelectedIndex = 1;
        RefreshGifStatus();
    }

    private void RemoveGifs_Click(object sender, RoutedEventArgs e)
    {
        _pendingFullGif = null;
        _pendingPeekGif = null;
        _removeGifs = true;
        RefreshGifStatus();
    }

    private bool SaveGifChanges()
    {
        if (!_removeGifs && _pendingFullGif is null && _pendingPeekGif is null) return true;
        try
        {
            if (_removeGifs)
            {
                if (File.Exists(SkinGif.FullPath)) File.Delete(SkinGif.FullPath);
                if (File.Exists(SkinGif.PeekPath)) File.Delete(SkinGif.PeekPath);
            }
            else
            {
                foreach (var (source, destination) in new[]
                    { (_pendingFullGif, SkinGif.FullPath), (_pendingPeekGif, SkinGif.PeekPath) })
                {
                    if (source is null) continue;
                    if (!SkinGif.TryOpen(source, out _, out var error))
                    {
                        MessageBox.Show(this, error ?? "GIF 不可读取。", "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                    {
                        string temporary = destination + ".tmp";
                        File.Copy(source, temporary, true);
                        File.Move(temporary, destination, true);
                    }
                }
            }
            AtlasChanged = true; // 通知主窗口重新载入全部皮肤资源
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "GIF 保存失败：" + ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
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
            !int.TryParse(SizeBox.Text, out int scale) || scale is < 50 or > 200 ||
            !int.TryParse(DelayBox.Text, out int delay) || delay is < 100 or > 2000 ||
            !int.TryParse(WaterBox.Text, out int water) || water is < 10 or > 240 ||
            !int.TryParse(SittingBox.Text, out int sitting) || sitting is < 10 or > 240 ||
            !int.TryParse(QuietStartBox.Text, out int quietStart) || quietStart is < 0 or > 23 ||
            !int.TryParse(QuietEndBox.Text, out int quietEnd) || quietEnd is < 0 or > 23)
        {
            MessageBox.Show(this, "请按各字段标注的范围填写整数。", "输入有误", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!SaveAtlasChanges() || !SaveGifChanges()) return;
        SelectedSkin = (SkinTheme)SkinCombo.SelectedIndex;
        ThresholdPx = threshold;
        VisiblePercent = visible;
        BallScalePercent = scale;
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
