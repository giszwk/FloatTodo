using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FloatTodo.Models;
using FloatTodo.Services;

namespace FloatTodo.Views;

/// <summary>
/// 图片/GIF 皮肤。默认显示 idle GIF 第一帧；非隐藏时交替低频播放 waiting/review。
/// 半隐藏使用专用右侧扒边 peek.gif；左侧吸边时沿竖直轴镜像。
/// </summary>
public partial class CatSkinVisual : UserControl
{
    /// <summary>半隐藏时至少露出窗口宽度的 68%（约 65px），保证猫脸、耳朵、爪子和扒边墙面完整可见。</summary>
    public const int PeekMinVisiblePercent = 68;

    private readonly DispatcherTimer _idleTimer;
    private readonly DispatcherTimer _actionTimer;
    private readonly BitmapImage _fallbackFull;
    private readonly BitmapImage _fallbackPeek;
    private readonly BitmapImage _fallbackBlink;
    private readonly SkinGif? _bundledIdle;
    private readonly SkinGif? _bundledPeek;
    private readonly SkinGif? _bundledWaiting;
    private readonly SkinGif? _bundledReview;
    private SkinAtlas? _atlas;
    private SkinGif? _userFull;
    private SkinGif? _userPeek;
    private SkinGif? _playing;
    private bool _peek;
    private bool _nextReview;
    private int _frame;
    private int _pending;
    private TimeSpan _actionElapsed;

    public CatSkinVisual()
    {
        InitializeComponent();
        _fallbackFull = LoadResource("cat-visible.png");
        _fallbackPeek = LoadResource("cat-peek.png");
        _fallbackBlink = LoadResource("cat-peek-blink.png");

        // 四个内置动图以 Content 形式复制到输出目录，不依赖用户手动导入。
        _bundledIdle = LoadBuiltIn("idle.gif");
        _bundledPeek = LoadBuiltIn("peek.gif");
        _bundledWaiting = LoadBuiltIn("waiting.gif");
        _bundledReview = LoadBuiltIn("review.gif");

        _idleTimer = new DispatcherTimer(DispatcherPriority.Background);
        _idleTimer.Tick += (_, _) => BeginAction();
        _actionTimer = new DispatcherTimer(DispatcherPriority.Background);
        _actionTimer.Tick += (_, _) => AdvanceFrame();
        IsVisibleChanged += (_, _) => ResetAnimation();
        Unloaded += (_, _) => { _idleTimer.Stop(); _actionTimer.Stop(); };
        ApplyIdleArt();
    }

    private static BitmapImage LoadResource(string name)
    {
        var bitmap = new BitmapImage(new Uri("pack://application:,,,/FloatTodo;component/Assets/Skins/" + name));
        bitmap.Freeze();
        return bitmap;
    }

    private static SkinGif? LoadBuiltIn(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Skins", "LuoXiaoHei", name);
        return SkinGif.TryOpen(path, out var gif, out _) ? gif : null;
    }

    public void ReloadAtlas()
    {
        _atlas = SkinAtlas.TryOpen(SkinAtlas.StoragePath, out var atlas, out _) ? atlas : null;
        _userFull = SkinGif.TryOpen(SkinGif.FullPath, out var full, out _) ? full : null;
        _userPeek = SkinGif.TryOpen(SkinGif.PeekPath, out var peek, out _) ? peek : null;
        ResetAnimation();
    }

    public void SetDockState(bool peeking, DockSide dock)
    {
        bool changed = peeking != _peek;
        _peek = peeking;
        FullArtwork.Visibility = peeking ? Visibility.Collapsed : Visibility.Visible;
        PeekArtwork.Visibility = peeking ? Visibility.Visible : Visibility.Collapsed;
        CountBadge.Visibility = peeking || _pending <= 0 ? Visibility.Collapsed : Visibility.Visible;
        // peek.gif 的原始方向是「右侧屏幕边缘」：猫脸朝左。
        // 吸附到左侧时沿竖直轴镜像（ScaleX=-1，绝不上下倒置）。
        PeekScale.ScaleX = dock == DockSide.Left ? -1 : 1;
        if (changed) ResetAnimation(); // 避免每次悬浮位置更新都重置倒计时
    }

    public void ShowPendingCount(int count)
    {
        _pending = count;
        CountText.Text = count > 99 ? "99+" : count.ToString();
        CountBadge.Visibility = _peek || count <= 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ResetAnimation()
    {
        _idleTimer.Stop();
        _actionTimer.Stop();
        _playing = null;
        _frame = 0;
        _actionElapsed = TimeSpan.Zero;
        FullImageMove.X = FullImageMove.Y = 0;
        ApplyIdleArt();
        if (IsVisible) ScheduleNext();
    }

    private void ScheduleNext()
    {
        // 隐藏：60–120 秒；展开：45–100 秒。没有连续循环，也没有强制闪烁。
        _idleTimer.Interval = TimeSpan.FromSeconds(_peek
            ? Random.Shared.Next(60, 121) : Random.Shared.Next(45, 101));
        _idleTimer.Start();
    }

    private void BeginAction()
    {
        _idleTimer.Stop();
        _frame = 0;
        _actionElapsed = TimeSpan.Zero;
        _playing = _peek ? _userPeek ?? _bundledPeek
            : _userFull ?? (_nextReview ? _bundledReview : _bundledWaiting);

        if (_playing is { Frames.Count: > 1 } anim)
        {
            // 第一帧立即出现，而不是等一个计时器周期再显示第二帧。
            if (_peek) PeekImage.Source = anim.Frames[0];
            else FullImage.Source = anim.Frames[0];
            _actionTimer.Interval = anim.Durations[0];
            _actionTimer.Start();
            return;
        }

        // 兼容自行导入的旧 PNG 图集、以及资源缺失时的图片兜底。
        _actionTimer.Interval = TimeSpan.FromMilliseconds(_peek ? 150 : 220);
        _actionTimer.Start();
    }

    private void AdvanceFrame()
    {
        _actionElapsed += _actionTimer.Interval;
        _frame++;
        if (_actionElapsed >= TimeSpan.FromSeconds(3)) { EndAction(); return; }

        if (_playing is { Frames.Count: > 1 } anim)
        {
            if (_frame >= anim.Frames.Count) { EndAction(); return; }
            if (_peek) PeekImage.Source = anim.Frames[_frame];
            else FullImage.Source = anim.Frames[_frame];
            _actionTimer.Interval = anim.Durations[_frame];
            return;
        }

        if (_peek)
        {
            PeekImage.Source = _frame is 1 or 2 ? _fallbackBlink : _fallbackPeek;
            if (_frame >= 3) EndAction();
            return;
        }
        if (_atlas is not null && _frame <= 6)
        {
            FullImage.Source = _atlas.Frame(_nextReview ? 8 : 6, _frame - 1);
        }
        if (_frame >= 6) EndAction();
    }

    private void ApplyIdleArt()
    {
        // 核心需求：展开时始终默认使用原始 idle GIF 的第 0 帧。
        FullImage.Source = _userFull is not null ? _userFull.Frames[0]
            : _atlas is not null ? _atlas.Frame(0, 0)
            : _bundledIdle is not null ? _bundledIdle.Frames[0] : _fallbackFull;
        // 半隐藏时保持专用 peek.gif 第一帧静止，低频才完整播放一次眨眼。
        PeekImage.Source = _userPeek is not null ? _userPeek.Frames[0]
            : _bundledPeek is not null ? _bundledPeek.Frames[0] : _fallbackPeek;
    }

    private void EndAction()
    {
        _actionTimer.Stop();
        if (!_peek) _nextReview = !_nextReview;
        _playing = null;
        _frame = 0;
        FullImageMove.X = FullImageMove.Y = 0;
        ApplyIdleArt();
        if (IsVisible) ScheduleNext();
    }
}
