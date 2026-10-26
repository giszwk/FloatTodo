using System;
using System.Windows;
using System.Windows.Threading;
using FloatTodo.Services;

namespace FloatTodo;

public partial class NotificationWindow : Window
{
    private readonly DispatcherTimer _hideTimer;
    public ReminderNotice Notice { get; }
    public event Action<ReminderNotice>? Completed;
    public event Action<ReminderNotice>? Snoozed;

    public NotificationWindow(ReminderNotice notice)
    {
        Notice = notice;
        InitializeComponent();
        HeadingLabel.Text = notice.Heading;
        DetailsLabel.Text = notice.Details;
        CompleteButton.Content = notice.Kind switch
        {
            NoticeKind.Water => "我喝过了 ✓",
            NoticeKind.Sitting => "我已活动 ✓",
            _ => "完成任务 ✓"
        };
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _hideTimer.Tick += (_, _) => Close();
        Loaded += (_, _) => _hideTimer.Start();
        Closed += (_, _) => _hideTimer.Stop();
    }

    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        Completed?.Invoke(Notice);
        Close();
    }
    private void Snooze_Click(object sender, RoutedEventArgs e)
    {
        Snoozed?.Invoke(Notice);
        Close();
    }
    private void Dismiss_Click(object sender, RoutedEventArgs e) => Close();
}
