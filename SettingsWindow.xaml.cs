using System.Windows;
using FloatTodo.Models;

namespace FloatTodo;

public partial class SettingsWindow : Window
{
    public int ThresholdPx { get; private set; }
    public int VisiblePercent { get; private set; }
    public int CollapseDelayMs { get; private set; }

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        DistanceBox.Text = settings.DockThresholdPx.ToString();
        VisibleBox.Text = settings.VisiblePercent.ToString();
        DelayBox.Text = settings.CollapseDelayMs.ToString();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(DistanceBox.Text, out int threshold) || threshold is < 8 or > 120 ||
            !int.TryParse(VisibleBox.Text, out int visible) || visible is < 20 or > 80 ||
            !int.TryParse(DelayBox.Text, out int delay) || delay is < 100 or > 2000)
        {
            MessageBox.Show(this, "请按标注的范围填写整数。", "输入有误",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ThresholdPx = threshold;
        VisiblePercent = visible;
        CollapseDelayMs = delay;
        DialogResult = true;
    }
}
