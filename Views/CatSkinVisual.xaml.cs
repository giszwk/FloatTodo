using System;
using System.Collections.Generic;
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
/// 展开状态点击悬浮球时随机播放 idle/waiting/review 之一。
/// 自定义主题从 %LOCALAPPDATA%\FloatTodo\skins\custom 读取同名 GIF，缺失项回退内置素材。
/// 半隐藏使用专用右侧扒边 peek.gif；左侧吸边时沿竖直轴镜像。
/// </summary>
public partial class CatSkinVisual : UserControl
{
    /// <summary>半隐藏时至少露出窗口宽度的 68%（约 65px），保证猫脸、耳朵、爪子和扒边墙面完整可见。</summary>
    public const int PeekMinVisiblePercent = 68;

    private readonly DispatcherTimer _idleTimer;
    private readonly DispatcherTimer _actionTimer;
    private SkinGif? _bundledIdle;
    private SkinGif? _bundledPeek;
    private SkinGif? _bundledWaiting;
    private SkinGif? _bundledReview;
    private SkinTheme _theme = SkinTheme.BlackCat;
    private string? _customDir;
    private SkinAtlas? _atlas;
    private SkinGif? _userFull;
    private SkinGif? _userPeek;
    private SkinGif? _playing;
    private bool _peek;
    private bool _nextReview;
    private bool _manualAction;
    private int _frame;
    private int _pending;
    private TimeSpan _actionElapsed;

    public CatSkinVisual()
    {
        InitializeComponent();

        // 先按内置皮肤载入；主窗口随后调用 ApplySkin 应用用户选择的主题。
        LoadThemeSources();

        _idleTimer = new DispatcherTimer(DispatcherPriority.Background);
        _idleTimer.Tick += (_, _) => BeginAction();
        _actionTimer = new DispatcherTimer(DispatcherPriority.Background);
        _actionTimer.Tick += (_, _) => AdvanceFrame();
        IsVisibleChanged += (_, _) => ResetAnimation();
        Unloaded += (_, _) => { _idleTimer.Stop(); _actionTimer.Stop(); };
        ApplyIdleArt();
    }

    /// <summary>按比例缩放整只猫（含角标和扒边位置）。</summary>
    public void SetUiScale(double scale)
    {
        RootScale.ScaleX = scale;
        RootScale.ScaleY = scale;
    }

    private static string BuiltInDir => Path.Combine(AppContext.BaseDirectory, "Assets", "Skins", "LuoXiaoHei");

    /// <summary>
    /// 切换皮肤主题并重新读取素材。自定义皮肤要求 4 个 GIF 全部有效，
    /// 任一缺失或损坏时回退默认蓝色悬浮球。
    /// </summary>
    public SkinTheme ApplySkin(SkinTheme requested)
    {
        var theme = requested;
        _customDir = theme == SkinTheme.Custom ? ResolveCustomSkinDir() : null;
        if (theme == SkinTheme.Custom && _customDir is null)
            theme = SkinTheme.DefaultBall;

        _theme = theme;
        LoadThemeSources();
        if (theme == SkinTheme.Custom
            && (_bundledIdle is null || _bundledPeek is null || _bundledWaiting is null || _bundledReview is null))
        {
            // 校验后文件被删除/占用等极端情况，同样整体回退。
            theme = SkinTheme.DefaultBall;
            _theme = theme;
            LoadThemeSources();
        }

        // 旧的导入机制只用于内置罗小黑皮肤；自定义皮肤以 skins\custom 目录为准。
        bool blackCat = theme == SkinTheme.BlackCat;
        _atlas = blackCat && SkinAtlas.TryOpen(SkinAtlas.StoragePath, out var atlas, out _) ? atlas : null;
        _userFull = blackCat && SkinGif.TryOpen(SkinGif.FullPath, out var full, out _) ? full : null;
        _userPeek = blackCat && SkinGif.TryOpen(SkinGif.PeekPath, out var peek, out _) ? peek : null;
        ResetAnimation();
        return theme;
    }

    /// <summary>自定义皮肤必须同时具备 idle/waiting/review/peek 四个有效 GIF。</summary>
    public static bool IsCustomSkinComplete() => ResolveCustomSkinDir() is not null;

    /// <summary>优先使用随程序分发的目录，其次兼容旧的 %LOCALAPPDATA% 目录。</summary>
    public static string? ResolveCustomSkinDir()
    {
        if (IsCustomSkinComplete(SkinGif.PortableCustomDir)) return SkinGif.PortableCustomDir;
        if (IsCustomSkinComplete(SkinGif.UserCustomDir)) return SkinGif.UserCustomDir;
        return null;
    }

    private static bool IsCustomSkinComplete(string dir)
    {
        foreach (var name in SkinGif.CustomFileNames)
            if (!SkinGif.TryOpen(Path.Combine(dir, name), out _, out _))
                return false;
        return true;
    }

    private void LoadThemeSources()
    {
        string primary = _theme == SkinTheme.Custom && _customDir is not null ? _customDir : BuiltInDir;
        _bundledIdle = LoadThemeGif(primary, "idle.gif");
        _bundledPeek = LoadThemeGif(primary, "peek.gif");
        _bundledWaiting = LoadThemeGif(primary, "waiting.gif");
        _bundledReview = LoadThemeGif(primary, "review.gif");
    }

    private static SkinGif? LoadThemeGif(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        return SkinGif.TryOpen(path, out var gif, out _) ? gif : null;
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

    /// <summary>
    /// 展开状态点击悬浮球：在内置 idle / waiting / review 中随机播放一段。
    /// 若用户导入了自定义展开 GIF，则播放该段，保持皮肤一致。
    /// </summary>
    public void PlayRandomAction()
    {
        if (_peek || !IsVisible) return;

        SkinGif? pick;
        if (_userFull is not null) pick = _userFull;
        else
        {
            var candidates = new List<SkinGif>();
            if (_bundledIdle is not null) candidates.Add(_bundledIdle);
            if (_bundledWaiting is not null) candidates.Add(_bundledWaiting);
            if (_bundledReview is not null) candidates.Add(_bundledReview);
            if (candidates.Count == 0) return;
            pick = candidates[Random.Shared.Next(candidates.Count)];
        }

        // 手动动作不参与 waiting/review 的自动交替。
        _manualAction = true;
        BeginAction(pick);
    }

    private void BeginAction()
    {
        _manualAction = false;
        BeginAction(_peek ? _userPeek ?? _bundledPeek
            : _userFull ?? (_nextReview ? _bundledReview : _bundledWaiting));
    }

    private void BeginAction(SkinGif? gif)
    {
        _idleTimer.Stop();
        _actionTimer.Stop();
        _frame = 0;
        _actionElapsed = TimeSpan.Zero;
        _playing = gif;

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
            // 内置 peek GIF 缺失时没有可播放的帧，直接结束本轮。
            EndAction();
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
        var full = _userFull?.Frames[0] ?? _atlas?.Frame(0, 0) ?? _bundledIdle?.Frames[0];
        if (full is not null) FullImage.Source = full;
        // 半隐藏时保持专用 peek.gif 第一帧静止，低频才完整播放一次眨眼。
        var peek = _userPeek?.Frames[0] ?? _bundledPeek?.Frames[0];
        if (peek is not null) PeekImage.Source = peek;
    }

    private void EndAction()
    {
        _actionTimer.Stop();
        if (!_peek && !_manualAction) _nextReview = !_nextReview;
        _manualAction = false;
        _playing = null;
        _frame = 0;
        FullImageMove.X = FullImageMove.Y = 0;
        ApplyIdleArt();
        if (IsVisible) ScheduleNext();
    }
}
