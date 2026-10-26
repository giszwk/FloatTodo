using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FloatTodo.Models;
using FloatTodo.Services;

namespace FloatTodo.Views;

public partial class CatSkinVisual : UserControl
{
    private readonly DispatcherTimer _idleTimer;
    private readonly DispatcherTimer _actionTimer;
    private bool _peek;
    private bool _nextReview;
    private int _frame;
    private int _pending;
    private SkinAtlas? _atlas;

    public CatSkinVisual()
    {
        InitializeComponent();
        _idleTimer = new DispatcherTimer(DispatcherPriority.Background)
        { Interval = TimeSpan.FromSeconds(60) };
        _idleTimer.Tick += (_, _) => BeginAction();
        _actionTimer = new DispatcherTimer(DispatcherPriority.Background)
        { Interval = TimeSpan.FromMilliseconds(160) };
        _actionTimer.Tick += (_, _) => AdvanceFrame();
        IsVisibleChanged += (_, _) => ResetAnimation();
        Unloaded += (_, _) => { _idleTimer.Stop(); _actionTimer.Stop(); };
    }

    public void ReloadAtlas()
    {
        _atlas = SkinAtlas.TryOpen(SkinAtlas.StoragePath, out var atlas, out _) ? atlas : null;
        ApplyIdleArt();
    }

    public void SetDockState(bool peeking, DockSide dock)
    {
        _peek = peeking;
        FullArtwork.Visibility = peeking ? Visibility.Collapsed : Visibility.Visible;
        PeekArtwork.Visibility = peeking ? Visibility.Visible : Visibility.Collapsed;
        CountBadge.Visibility = peeking || _pending <= 0 ? Visibility.Collapsed : Visibility.Visible;

        // 左边吸附时窗口右侧40%左右可见；右边吸附时窗口左侧可见。
        // 头向可见条带偏移22像素，让小黑猫自然从边缘探出脸。
        var direction = dock == DockSide.Right ? -1 : 1;
        PeekCat.RenderTransformOrigin = new Point(.5, .5);
        var transforms = new System.Windows.Media.TransformGroup();
        transforms.Children.Add(new System.Windows.Media.ScaleTransform(direction < 0 ? -1 : 1, 1));
        transforms.Children.Add(new System.Windows.Media.TranslateTransform(22 * direction, 0));
        PeekCat.RenderTransform = transforms;
        ResetAnimation();
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
        _frame = 0;
        PeekBlink.ScaleY = 1;
        HeadMove.X = HeadMove.Y = 0;
        LeftPupilMove.X = RightPupilMove.X = 0;
        ApplyIdleArt();
        if (IsVisible)
            ScheduleNext();
    }

    private void ScheduleNext()
    {
        // 稀疏、无循环位移的动画；眨眼只需约0.32秒。
        _idleTimer.Interval = TimeSpan.FromSeconds(_peek ? Random.Shared.Next(48, 96) : Random.Shared.Next(38, 81));
        _idleTimer.Start();
    }

    private void BeginAction()
    {
        _idleTimer.Stop();
        _frame = 0;
        _actionTimer.Interval = TimeSpan.FromMilliseconds(_peek ? 120 : 160);
        _actionTimer.Start();
    }

    private void AdvanceFrame()
    {
        _frame++;
        if (_peek)
        {
            // 3个计时帧，在原地闭眼-睁眼，不位移头部或爪子。
            PeekBlink.ScaleY = _frame == 1 ? .10 : 1;
            if (_frame >= 3) EndAction();
            return;
        }

        // 两种短动作交替：Waiting（向侧面看一眼）、Review（轻轻点头）。
        // 动作结束后恢复完全静止，避免一直动引人注意。
        int pose = _frame <= 3 ? _frame : 6 - _frame;
        if (_nextReview)
        {
            HeadMove.Y = pose * .48;
            HeadMove.X = 0;
            LeftPupilMove.X = RightPupilMove.X = 0;
            if (_atlas is not null && _frame <= 6) AtlasImage.Source = _atlas.Frame(8, Math.Min(5, _frame - 1));
        }
        else
        {
            HeadMove.Y = 0;
            HeadMove.X = pose * .42;
            LeftPupilMove.X = RightPupilMove.X = pose * .5;
            if (_atlas is not null && _frame <= 6) AtlasImage.Source = _atlas.Frame(6, Math.Min(5, _frame - 1));
        }
        if (_frame >= 6)
        {
            _nextReview = !_nextReview;
            EndAction();
        }
    }

    private void ApplyIdleArt()
    {
        // 导入图集有透明区域，必须隐藏原生矢量图，避免两种形象透叠。
        foreach (UIElement child in FullArtwork.Children)
        {
            if (!ReferenceEquals(child, AtlasImage))
                child.Visibility = _atlas is null ? Visibility.Visible : Visibility.Collapsed;
        }
        AtlasImage.Visibility = _atlas is null ? Visibility.Collapsed : Visibility.Visible;
        AtlasImage.Source = _atlas?.Frame(0, 0);
    }

    private void EndAction()
    {
        _actionTimer.Stop();
        _frame = 0;
        PeekBlink.ScaleY = 1;
        HeadMove.X = HeadMove.Y = 0;
        LeftPupilMove.X = RightPupilMove.X = 0;
        ApplyIdleArt();
        if (IsVisible) ScheduleNext();
    }
}
