using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FloatTodo.Interop;
using FloatTodo.Models;
using FloatTodo.Services;
using FloatTodo.Views;
using Forms = System.Windows.Forms;

namespace FloatTodo;

public partial class MainWindow : Window
{
    private readonly TaskRepository _repository;
    private readonly SettingsStore _settings;
    private readonly ReminderService _reminders;
    private NotificationWindow? _notice;
    private readonly TasksWindow _panel;
    private readonly DispatcherTimer _collapseTimer;
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _trayVisibilityItem;
    private bool _pressed;
    private bool _dragging;
    private bool _settingsOpen;
    private bool _exiting;
    private NativeWindow.PixelPoint _pressCursor;
    private NativeWindow.PixelRect _pressRect;
    private DockSide _dock;
    private NativeWindow.PixelRect _dockWork;
    private bool _expanded;

    public MainWindow(TaskRepository repository, SettingsStore settings)
    {
        _repository = repository;
        _settings = settings;
        _dock = settings.Value.Dock;
        InitializeComponent();
        CatVisual.ReloadAtlas();
        ApplySkinVisual(false);

        // 先初始化计时器，再注册会访问计时器的面板事件，避免 CS8602 空引用警告。
        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(settings.Value.CollapseDelayMs) };
        _collapseTimer.Tick += (_, _) => MaybeCollapse();

        _panel = new TasksWindow(_repository);
        _panel.MouseEnter += (_, _) => _collapseTimer.Stop();
        _panel.MouseLeave += (_, _) => BeginCollapse();
        _panel.TasksChanged += UpdateBadge;
        _panel.EditorClosed += BeginCollapse;
        _panel.DragFinished += BeginCollapse;

        _reminders = new ReminderService(_repository, _settings);
        _reminders.TasksGenerated += () => { _panel.RefreshItems(); UpdateBadge(); };
        _reminders.NoticeReady += ShowReminder;
        _reminders.SchedulerError += message =>
            MessageBox.Show($"提醒调度已暂停：{message}", "FloatTodo", MessageBoxButton.OK, MessageBoxImage.Warning);

        BuildContextMenu();
        Loaded += (_, _) => { InitializePosition(); _reminders.Start(); };
        Closed += (_, _) => Cleanup();
        CreateTray();
        UpdateBadge();
    }

    private void InitializePosition()
    {
        var area = NativeWindow.WorkAreaNear(
            _settings.Value.PositionX ?? NativeWindow.Cursor().X,
            _settings.Value.PositionY ?? NativeWindow.Cursor().Y);
        _dockWork = area;
        var rect = NativeWindow.Rect(this);
        int initialX = _settings.Value.PositionX ?? (area.Right - rect.Width - 24);
        int initialY = _settings.Value.PositionY ?? (area.Top + 90);
        int x = Math.Clamp(initialX, area.Left, Math.Max(area.Left, area.Right - rect.Width));
        int y = Math.Clamp(initialY, area.Top, Math.Max(area.Top, area.Bottom - rect.Height));
        NativeWindow.Move(this, x, y);
        if (_dock != DockSide.None)
            SnapToEdge();
    }

    private void Ball_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_pressed) return;
        _collapseTimer.Stop();
        ExpandFromEdge();
        ShowPanel();
    }

    private void Ball_MouseLeave(object sender, MouseEventArgs e) => BeginCollapse();

    private void Ball_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1) e.Handled = true;
        _collapseTimer.Stop();
        _pressed = true;
        _dragging = false;
        ExpandFromEdge();
        _pressCursor = NativeWindow.Cursor();
        _pressRect = NativeWindow.Rect(this);
        Mouse.Capture(BallRoot);
        e.Handled = true;
    }

    private void Ball_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_pressed) return;
        var mouse = NativeWindow.Cursor();
        int dx = mouse.X - _pressCursor.X;
        int dy = mouse.Y - _pressCursor.Y;
        if (!_dragging && Math.Abs(dx) + Math.Abs(dy) < 5) return;
        if (!_dragging)
        {
            _dragging = true;
            _dock = DockSide.None;
            _expanded = false;
            _panel.Hide();
            CatVisual.SetDockState(false, DockSide.None);
        }
        NativeWindow.Move(this, _pressRect.Left + dx, _pressRect.Top + dy);
    }

    private void Ball_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed) return;
        _pressed = false;
        Mouse.Capture(null);
        if (_dragging)
        {
            _dragging = false;
            CompleteDrag();
        }
        else
            ShowPanel();
        e.Handled = true;
    }

    private void CompleteDrag()
    {
        var rect = NativeWindow.Rect(this);
        var area = NativeWindow.WorkAreaNear(rect.CenterX, rect.CenterY);
        _dockWork = area;
        int x = Math.Clamp(rect.Left, area.Left, Math.Max(area.Left, area.Right - rect.Width));
        int y = Math.Clamp(rect.Top, area.Top, Math.Max(area.Top, area.Bottom - rect.Height));
        NativeWindow.Move(this, x, y);
        // 以松开位置（原始矩形）判断距离；拖过屏幕边缘时按 0 计算，
        // 否则 Math.Abs 会把“超出边缘”误判成“离边缘很远”，导致时好时坏的吸附。
        int leftGap = Math.Max(0, rect.Left - area.Left);
        int rightGap = Math.Max(0, area.Right - rect.Right);
        int threshold = _settings.Value.DockThresholdPx;
        if (Math.Min(leftGap, rightGap) <= threshold)
        {
            _dock = leftGap <= rightGap ? DockSide.Left : DockSide.Right;
            // 松开鼠标时不立刻半隐藏；待鼠标移出后才收起。
            _expanded = true;
        }
        else
            _dock = DockSide.None;
        CatVisual.SetDockState(false, _dock);
        SavePosition();
        if (_dock != DockSide.None) BeginCollapse();
    }

    private void ExpandFromEdge()
    {
        if (_dock == DockSide.None || _expanded || _dragging) return;
        var rect = NativeWindow.Rect(this);
        var area = _dockWork;
        int x = _dock == DockSide.Left ? area.Left : area.Right - rect.Width;
        NativeWindow.Move(this, x, rect.Top);
        _expanded = true;
        CatVisual.SetDockState(false, _dock);
    }

    private void SnapToEdge()
    {
        if (_dock == DockSide.None) return;
        var rect = NativeWindow.Rect(this);
        var area = _dockWork;
        int visiblePercent = _settings.Value.VisiblePercent;
        // 罗小黑扒边图较宽：按用户比例可能只露出半张脸，68% 才能放下整只猫和扒边墙面。
        if (_settings.Value.Skin == SkinTheme.BlackCat)
            visiblePercent = Math.Max(visiblePercent, CatSkinVisual.PeekMinVisiblePercent);
        int visible = Math.Max(9, rect.Width * visiblePercent / 100);
        int x = _dock == DockSide.Left
            ? area.Left - (rect.Width - visible)
            : area.Right - visible;
        int y = Math.Clamp(rect.Top, area.Top, Math.Max(area.Top, area.Bottom - rect.Height));
        NativeWindow.Move(this, x, y);
        _expanded = false;
        CatVisual.SetDockState(true, _dock);
    }

    private void ShowPanel()
    {
        if (_dragging || _settingsOpen || _panel.IsEditing) return;
        _collapseTimer.Stop();
        ExpandFromEdge();
        if (!_panel.IsVisible)
        {
            _panel.RefreshItems();
            _panel.Opacity = 0;
            _panel.Show();
            PositionPanel();
            _panel.Opacity = 1;
        }
        else
            PositionPanel();
    }

    private void PositionPanel()
    {
        if (!_panel.IsVisible) return;
        var ball = NativeWindow.Rect(this);
        var popup = NativeWindow.Rect(_panel);
        var area = NativeWindow.WorkAreaNear(ball.CenterX, ball.CenterY);
        int gap = 10;
        int freeRight = area.Right - ball.Right - gap;
        int freeLeft = ball.Left - area.Left - gap;
        int x = freeRight >= popup.Width || freeRight >= freeLeft
            ? ball.Right + gap
            : ball.Left - popup.Width - gap;
        x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - popup.Width));
        int y = Math.Clamp(ball.Top - 12, area.Top,
            Math.Max(area.Top, area.Bottom - popup.Height));
        NativeWindow.Move(_panel, x, y);
    }

    private void BeginCollapse()
    {
        if (_pressed || _settingsOpen || _panel.IsEditing || _panel.IsDragging) return;
        _collapseTimer.Interval = TimeSpan.FromMilliseconds(_settings.Value.CollapseDelayMs);
        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    private void MaybeCollapse()
    {
        _collapseTimer.Stop();
        if (_pressed || _settingsOpen || _panel.IsEditing || _panel.IsDragging) return;
        // Win32 物理像素坐标判定，避免透明窗口和跨 DPI 鼠标事件误判。
        var cursor = NativeWindow.Cursor();
        if ((IsVisible && NativeWindow.Contains(NativeWindow.Rect(this), cursor)) ||
            (_panel.IsVisible && NativeWindow.Contains(NativeWindow.Rect(_panel), cursor)))
            return;
        _panel.Hide();
        SnapToEdge();
    }

    private void UpdateBadge()
    {
        int count = _repository.GetPendingCount();
        BallVisual.ShowPendingCount(count);
        CatVisual.ShowPendingCount(count);
    }

    private void SavePosition()
    {
        var rect = NativeWindow.Rect(this);
        var area = _dock == DockSide.None
            ? NativeWindow.WorkAreaNear(rect.CenterX, rect.CenterY)
            : _dockWork;
        var value = _settings.Value;
        value.Dock = _dock;
        value.PositionX = _dock switch
        {
            DockSide.Left => area.Left,
            DockSide.Right => area.Right - rect.Width,
            _ => rect.Left
        };
        value.PositionY = rect.Top;
        _settings.Save();
    }

    // 皮肤与窗口尺寸分离。桌面拖动/吸边始终操作主窗口，不与具体绘图绑定。
    private void ApplySkinVisual(bool reposition)
    {
        bool cat = _settings.Value.Skin == SkinTheme.BlackCat;
        NativeWindow.PixelRect before = default;
        if (reposition && IsLoaded) before = NativeWindow.Rect(this);
        BallVisual.Visibility = cat ? Visibility.Collapsed : Visibility.Visible;
        CatVisual.Visibility = cat ? Visibility.Visible : Visibility.Collapsed;
        Width = cat ? 96 : 64;
        Height = cat ? 96 : 64;
        CatVisual.SetDockState(_dock != DockSide.None && !_expanded, _dock);

        if (!reposition || !IsLoaded) return;
        // 先让 WPF 完成 Window 的实际尺寸变化，再按物理像素重新定位。
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var after = NativeWindow.Rect(this);
            var area = NativeWindow.WorkAreaNear(before.CenterX, before.CenterY);
            _dockWork = area;
            if (_dock != DockSide.None)
            {
                NativeWindow.Move(this,
                    _dock == DockSide.Left ? area.Left : area.Right - after.Width,
                    Math.Clamp(before.Top, area.Top, Math.Max(area.Top, area.Bottom - after.Height)));
                if (!_expanded) SnapToEdge();
                else CatVisual.SetDockState(false, _dock);
            }
            else
            {
                int targetX = Math.Clamp(before.CenterX - after.Width / 2, area.Left,
                    Math.Max(area.Left, area.Right - after.Width));
                int targetY = Math.Clamp(before.CenterY - after.Height / 2, area.Top,
                    Math.Max(area.Top, area.Bottom - after.Height));
                NativeWindow.Move(this, targetX, targetY);
            }
            SavePosition();
        }), DispatcherPriority.Loaded);
    }

    private void SwitchSkin(SkinTheme skin)
    {
        if (_settings.Value.Skin == skin) return;
        _settings.Value.Skin = skin;
        _settings.Save();
        ApplySkinVisual(true);
    }

    private void BuildContextMenu()
    {
        var context = new ContextMenu();
        var tasks = new MenuItem { Header = "打开待办" };
        tasks.Click += (_, _) => ShowPanel();
        var planned = new MenuItem { Header = "新建计划任务" };
        planned.Click += (_, _) => { ShowPanel(); _panel.OpenPlanDialog(); };
        var settings = new MenuItem { Header = "设置" };
        settings.Click += (_, _) => OpenSettings();
        var hide = new MenuItem { Header = "隐藏悬浮球（可在托盘恢复）" };
        hide.Click += (_, _) => HideBall();
        var exit = new MenuItem { Header = "退出程序" };
        exit.Click += (_, _) => ExitApp();
        context.Items.Add(tasks);
        context.Items.Add(planned);
        context.Items.Add(settings);
        context.Items.Add(new Separator());
        context.Items.Add(hide);
        context.Items.Add(exit);
        BallRoot.ContextMenu = context;
    }

    private void CreateTray()
    {
        var iconFile = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        _tray = new Forms.NotifyIcon
        {
            Text = "FloatTodo · 桌面待办",
            Icon = File.Exists(iconFile)
                ? new System.Drawing.Icon(iconFile)
                : System.Drawing.SystemIcons.Application,
            Visible = true
        };
        var menu = new Forms.ContextMenuStrip();
        _trayVisibilityItem = new Forms.ToolStripMenuItem("隐藏悬浮球");
        _trayVisibilityItem.Click += (_, _) => Dispatcher.Invoke(ToggleBallVisibility);
        menu.Items.Add(_trayVisibilityItem);
        menu.Items.Add("打开待办", null, (_, _) => Dispatcher.Invoke(() => { ShowBall(); ShowPanel(); }));
        menu.Items.Add("新建计划任务", null, (_, _) => Dispatcher.Invoke(() => { ShowBall(); ShowPanel(); _panel.OpenPlanDialog(); }));
        menu.Items.Add("设置", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        // 皮肤仅在设置窗口修改，防止两个入口不同步。
        menu.Opening += (_, _) => RefreshTrayVisibilityText();
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApp));
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => { ShowBall(); ShowPanel(); });
    }

    private void RefreshTrayVisibilityText()
    {
        if (_trayVisibilityItem is not null)
            _trayVisibilityItem.Text = IsVisible ? "隐藏悬浮球" : "显示悬浮球";
    }

    private void ToggleBallVisibility()
    {
        if (IsVisible) HideBall();
        else ShowBall();
    }

    private void HideBall()
    {
        _collapseTimer.Stop();
        _panel.Hide();
        Hide();
        RefreshTrayVisibilityText();
    }

    private void ShowBall()
    {
        if (!IsVisible)
        {
            Show();
            Topmost = true;
            // 临时隐藏后只需恢复窗口，不重新应用旧位置。
        }
        RefreshTrayVisibilityText();
    }

    private void OpenSettings()
    {
        _settingsOpen = true;
        _collapseTimer.Stop();
        _panel.Hide();
        try
        {
            var dialog = new SettingsWindow(_settings.Value);
            if (dialog.ShowDialog() == true)
            {
                _settings.Value.DockThresholdPx = dialog.ThresholdPx;
                _settings.Value.VisiblePercent = dialog.VisiblePercent;
                _settings.Value.CollapseDelayMs = dialog.CollapseDelayMs;
                _settings.Value.WaterEnabled = dialog.WaterEnabled;
                _settings.Value.WaterIntervalMinutes = dialog.WaterMinutes;
                _settings.Value.SittingEnabled = dialog.SittingEnabled;
                _settings.Value.SittingIntervalMinutes = dialog.SittingMinutes;
                _settings.Value.ReminderSound = dialog.SoundEnabled;
                _settings.Value.SuppressFullscreen = dialog.SuppressFullscreen;
                _settings.Value.QuietStartHour = dialog.QuietStartHour;
                _settings.Value.QuietEndHour = dialog.QuietEndHour;
                var skinChanged = _settings.Value.Skin != dialog.SelectedSkin;
                _settings.Value.Skin = dialog.SelectedSkin;
                _settings.Save();
                if (dialog.AtlasChanged) CatVisual.ReloadAtlas();
                if (skinChanged) ApplySkinVisual(true);
                _reminders.SettingsChanged();
                if (_dock != DockSide.None && !skinChanged)
                    SnapToEdge();
            }
        }
        finally { _settingsOpen = false; }
    }

    private void ShowReminder(ReminderNotice notice)
    {
        if (_exiting)
        {
            _reminders.NoticeDismissed();
            return;
        }
        _notice?.Close();
        _panel.Hide();
        var bubble = new NotificationWindow(notice);
        _notice = bubble;
        bubble.Completed += reminder =>
        {
            try
            {
                _reminders.Acknowledge(reminder);
                _panel.RefreshItems();
                UpdateBadge();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"更新提醒失败：{ex.Message}", "FloatTodo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        bubble.Snoozed += reminder =>
        {
            try { _reminders.Snooze(reminder); }
            catch (Exception ex)
            {
                MessageBox.Show($"稍后提醒设置失败：{ex.Message}", "FloatTodo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        bubble.Closed += (_, _) =>
        {
            if (ReferenceEquals(_notice, bubble)) _notice = null;
            _reminders.NoticeDismissed();
            if (!_exiting) BeginCollapse();
        };
        bubble.Show();
        bubble.UpdateLayout();
        PositionReminder(bubble);
        if (_settings.Value.ReminderSound)
        {
            try { System.Media.SystemSounds.Asterisk.Play(); }
            catch { /* 音频设备不可用时不影响提醒 */ }
        }
    }

    private void PositionReminder(NotificationWindow bubble)
    {
        var ball = NativeWindow.Rect(this);
        var rect = NativeWindow.Rect(bubble);
        var area = NativeWindow.WorkAreaNear(ball.CenterX, ball.CenterY);
        var right = ball.Right + 12;
        var left = ball.Left - rect.Width - 12;
        int x = right + rect.Width <= area.Right ? right : left;
        x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - rect.Width));
        int y = Math.Clamp(ball.Top - 20, area.Top, Math.Max(area.Top, area.Bottom - rect.Height));
        NativeWindow.Move(bubble, x, y);
    }

    private void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        _collapseTimer.Stop();
        _reminders.Stop();
        SavePosition();
        System.Windows.Application.Current.Shutdown();
    }

    private void Cleanup()
    {
        _collapseTimer.Stop();
        _reminders.Stop();
        _notice?.Close();
        _panel.Close();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.ContextMenuStrip?.Dispose();
            _tray.Dispose();
        }
    }
}
