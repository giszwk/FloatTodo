using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using FloatTodo.Models;

namespace FloatTodo.Services;

public enum NoticeKind { Todo, Water, Sitting }

public sealed class ReminderNotice
{
    public NoticeKind Kind { get; init; }
    public string? TaskId { get; init; }
    public required string Heading { get; init; }
    public required string Details { get; init; }
}

/// <summary>统一调度器：任务到点、周期生成、喝水、键鼠空闲近似计时。</summary>
public sealed class ReminderService
{
    private readonly TaskRepository _repository;
    private readonly SettingsStore _settings;
    private readonly DispatcherTimer _timer;
    private DateTime _nextWater;
    private DateTime _lastCheck;
    private DateTime? _healthSnoozeWater;
    private DateTime? _healthSnoozeSitting;
    private double _activeSeconds;
    private bool _noticeShowing;

    public event Action<ReminderNotice>? NoticeReady;
    public event Action? TasksGenerated;
    public event Action<string>? SchedulerError;

    public ReminderService(TaskRepository repository, SettingsStore settings)
    {
        _repository = repository;
        _settings = settings;
        _lastCheck = DateTime.Now;
        _nextWater = _lastCheck.AddMinutes(settings.Value.WaterIntervalMinutes);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _timer.Tick += (_, _) => Pulse();
    }

    public void Start()
    {
        _timer.Start();
        Pulse();
    }

    public void Stop() => _timer.Stop();

    public void SettingsChanged()
    {
        _nextWater = DateTime.Now.AddMinutes(_settings.Value.WaterIntervalMinutes);
        _healthSnoozeWater = null;
        _healthSnoozeSitting = null;
        _activeSeconds = 0;
        _lastCheck = DateTime.Now;
    }

    public void NoticeDismissed() => _noticeShowing = false;

    public void Acknowledge(ReminderNotice notice)
    {
        if (notice.Kind == NoticeKind.Todo && notice.TaskId is { } id)
            _repository.SetCompleted(id, true);
        else if (notice.Kind == NoticeKind.Water)
        {
            _nextWater = DateTime.Now.AddMinutes(_settings.Value.WaterIntervalMinutes);
            _healthSnoozeWater = null;
        }
        else if (notice.Kind == NoticeKind.Sitting)
        {
            _activeSeconds = 0;
            _healthSnoozeSitting = null;
        }
    }

    public void Snooze(ReminderNotice notice)
    {
        var then = DateTime.Now.AddMinutes(10);
        if (notice.Kind == NoticeKind.Todo && notice.TaskId is { } id)
            _repository.Snooze(id, then);
        else if (notice.Kind == NoticeKind.Water)
            _healthSnoozeWater = then;
        else if (notice.Kind == NoticeKind.Sitting)
            _healthSnoozeSitting = then;
    }

    private bool IsQuiet(DateTime now)
    {
        var begin = _settings.Value.QuietStartHour;
        var end = _settings.Value.QuietEndHour;
        if (begin == end) return false; // 相同表示禁用免打扰时间段
        return begin < end ? now.Hour >= begin && now.Hour < end : now.Hour >= begin || now.Hour < end;
    }

    private void Pulse()
    {
        var now = DateTime.Now;
        var seconds = (now - _lastCheck).TotalSeconds;
        _lastCheck = now;
        // 睡眠/系统时间变更后的大时间差不计作久坐。
        if (seconds is < 0 or > 45) seconds = 0;
        try
        {
            // 先生成循环任务。免打扰仅屏蔽提醒，不阻止正常生成待办。
            if (_repository.AdvanceSchedules(now)) TasksGenerated?.Invoke();
            if (IsQuiet(now) || (_settings.Value.SuppressFullscreen && IsFullscreenApp()))
            {
                _activeSeconds = 0;
                _nextWater = now.AddMinutes(_settings.Value.WaterIntervalMinutes);
                return;
            }
            if (IdleSeconds() >= 300) _activeSeconds = 0;
            else _activeSeconds += seconds;
            if (_noticeShowing) return;
            var due = _repository.GetDueReminders(now);
            if (due.Count > 0)
            {
                // 电脑关闭/休眠时积累多个到期事件：合并成一张提醒，不连续刷屏。
                _repository.MarkAlerted(due.ConvertAll(d => d.Id));
                var first = due[0];
                Raise(new ReminderNotice
                {
                    Kind = NoticeKind.Todo, TaskId = first.Id,
                    Heading = "⏰ 任务到点了",
                    Details = due.Count == 1 ? $"{first.Title}\n时间：{first.DueLocal.Replace('T', ' ')}" :
                        $"{first.Title}（还有 {due.Count - 1} 项到期，请查看待办）"
                });
                return;
            }
            if (_settings.Value.WaterEnabled &&
                ((_healthSnoozeWater is { } retry && now >= retry) ||
                 (_healthSnoozeWater is null && now >= _nextWater)))
            {
                _nextWater = now.AddMinutes(_settings.Value.WaterIntervalMinutes);
                _healthSnoozeWater = null;
                Raise(new ReminderNotice { Kind = NoticeKind.Water, Heading = "🥤 该喝水啦！",
                    Details = "工作一段时间了，喝点水休息一下吧。" });
                return;
            }
            if (_settings.Value.SittingEnabled &&
                ((_healthSnoozeSitting is { } later && now >= later) ||
                 (_healthSnoozeSitting is null && _activeSeconds >= _settings.Value.SittingIntervalMinutes * 60)))
            {
                _activeSeconds = 0;
                _healthSnoozeSitting = null;
                Raise(new ReminderNotice { Kind = NoticeKind.Sitting, Heading = "🧘 起来活动一下！",
                    Details = "你已经连续使用电脑一段时间了，起来走一走吧。" });
            }
        }
        catch (Exception ex)
        {
            _timer.Stop(); // 不在后台反复重复报错
            SchedulerError?.Invoke(ex.Message);
        }
    }

    private void Raise(ReminderNotice notice)
    {
        _noticeShowing = true;
        NoticeReady?.Invoke(notice);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PixelRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public PixelRect Monitor;
        public PixelRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out PixelRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    private static bool IsFullscreenApp()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var window)) return false;
        var monitor = MonitorFromWindow(hwnd, 2);
        if (monitor == IntPtr.Zero) return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;
        const int tol = 2;
        return window.Left <= info.Monitor.Left + tol && window.Top <= info.Monitor.Top + tol &&
               window.Right >= info.Monitor.Right - tol && window.Bottom >= info.Monitor.Bottom - tol;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size; public uint Time; }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    private static double IdleSeconds()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return 0;
        // uint 运算支持 GetTickCount 在 49.7 天左右溢出。
        return unchecked((uint)Environment.TickCount - info.Time) / 1000.0;
    }
}
