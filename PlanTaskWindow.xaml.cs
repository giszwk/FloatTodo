using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FloatTodo.Models;

namespace FloatTodo;

public partial class PlanTaskWindow : Window
{
    public string TaskTitle => TitleBox.Text.Trim();
    public DateTime StartLocal { get; private set; }
    public RepeatKind Repeat { get; private set; }
    public bool Reminder => RemindCheck.IsChecked == true;

    public PlanTaskWindow(string? suggestedTitle = null)
    {
        InitializeComponent();
        TitleBox.Text = suggestedTitle ?? string.Empty;
        var start = DateTime.Now.AddMinutes(5);
        StartDate.SelectedDate = start.Date;
        TimeBox.Text = start.ToString("HH:mm", CultureInfo.InvariantCulture);
        Loaded += (_, _) => TitleBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (TaskTitle.Length is < 1 or > 200 || StartDate.SelectedDate is not { } date ||
            !DateTime.TryParseExact(TimeBox.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var clock) ||
            RepeatBox.SelectedItem is not ComboBoxItem item ||
            !Enum.TryParse(item.Tag as string, out RepeatKind repeat))
        {
            MessageBox.Show(this, "请填写任务名称、日期和 24 小时制时间（HH:mm）。", "输入有误",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var when = date.Date.AddHours(clock.Hour).AddMinutes(clock.Minute);
        if (when < DateTime.Now)
        {
            MessageBox.Show(this, "计划开始时间需要晚于当前时间。", "输入有误",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Repeat = repeat;
        StartLocal = when;
        DialogResult = true;
    }
}
