using System.Windows;
using FloatTodo.Models;

namespace FloatTodo;

public partial class SettingsWindow : Window
{
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
