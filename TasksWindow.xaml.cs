using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FloatTodo.Models;
using FloatTodo.Services;

namespace FloatTodo;

public partial class TasksWindow : Window
{
    private const string TaskDragFormat = "FloatTodo.Internal.TaskId";
    private readonly TaskRepository _repository;
    private FrameworkElement? _armedHandle;
    private TodoItem? _armedItem;
    private Point _dragStart;
    private TodoItem? _activeDrag;
    // WPF ItemsControl 的容器生成器可能暂时返回 null；保存真实行控件，
    // 不再错误地把未知位置当作“列表末尾”。
    private readonly Dictionary<string, FrameworkElement> _rows = new();

    public event Action? TasksChanged;
    public event Action? EditorClosed;
    public event Action? DragFinished;
    public bool IsEditing { get; private set; }
    public bool IsDragging => _activeDrag is not null;

    public TasksWindow(TaskRepository repository)
    {
        _repository = repository;
        InitializeComponent();
        RefreshItems();
    }

    public void RefreshItems()
    {
        var all = _repository.GetAll();
        var pending = all.Where(item => !item.IsCompleted).ToList();
        var completed = all.Where(item => item.IsCompleted).ToList();
        PendingList.ItemsSource = pending;
        CompletedList.ItemsSource = completed;
        PendingLabel.Text = $"{pending.Count} 项";
        CompletedLabel.Text = $"{completed.Count} 项";
        ProgressLabel.Text = all.Count == 0 ? "开始记录你的计划" : $"已完成 {completed.Count}/{all.Count}";
        PendingEmpty.Visibility = pending.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CompletedEmpty.Visibility = completed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var title = NewTaskBox.Text.Trim();
        if (title.Length == 0) return;
        Change(() => _repository.Add(title));
        NewTaskBox.Clear();
        NewTaskBox.Focus();
    }

    public void OpenPlanDialog() => Plan_Click(this, new RoutedEventArgs());

    private void Plan_Click(object sender, RoutedEventArgs e)
    {
        IsEditing = true;
        try
        {
            var dialog = new PlanTaskWindow(NewTaskBox.Text.Trim()) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                Change(() => _repository.CreatePlannedTask(dialog.TaskTitle, dialog.StartLocal,
                    dialog.Repeat, dialog.Reminder));
                NewTaskBox.Clear();
            }
        }
        finally { IsEditing = false; EditorClosed?.Invoke(); }
    }

    private void Schedules_Click(object sender, RoutedEventArgs e)
    {
        IsEditing = true;
        try { new SchedulesWindow(_repository) { Owner = this }.ShowDialog(); }
        finally { IsEditing = false; EditorClosed?.Invoke(); }
    }

    private void NewTaskBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Add_Click(sender, e); e.Handled = true; }
    }

    private void Complete_Checked(object sender, RoutedEventArgs e)
    {
        if (TagId(sender) is { } id) Change(() => _repository.SetCompleted(id, true));
    }
    private void Reopen_Unchecked(object sender, RoutedEventArgs e)
    {
        if (TagId(sender) is { } id) Change(() => _repository.SetCompleted(id, false));
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (TagId(sender) is { } id) Change(() => _repository.Delete(id));
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (TagId(sender) is not { } id) return;
        TodoItem? item = _repository.GetAll().FirstOrDefault(task => task.Id == id);
        if (item is null) return;
        IsEditing = true;
        try
        {
            var editor = new EditTaskWindow(item.Title) { Owner = this };
            if (editor.ShowDialog() == true)
                Change(() => _repository.Rename(id, editor.UpdatedTitle));
        }
        finally { IsEditing = false; EditorClosed?.Invoke(); }
    }

    private void DragHandle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || sender is not FrameworkElement handle ||
            handle.DataContext is not TodoItem item) return;
        ResetDragCandidate();
        _armedHandle = handle;
        _armedItem = item;
        _dragStart = e.GetPosition(this);
        handle.CaptureMouse();
        e.Handled = true;
    }

    private void DragHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (_armedHandle is null || _armedItem is null || !ReferenceEquals(sender, _armedHandle)) return;
        if (e.LeftButton != MouseButtonState.Pressed) { ResetDragCandidate(); return; }
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var handle = _armedHandle;
        var item = _armedItem;
        // 释放鼠标捕获会同步重派发输入事件；先清空候选状态，
        // 防止重入的 MouseMove 再次启动嵌套拖拽（会产生第二次 Drop 并落到列表末尾）。
        _armedHandle = null;
        _armedItem = null;
        handle.ReleaseMouseCapture();
        _activeDrag = item;
        try { DragDrop.DoDragDrop(handle, new DataObject(TaskDragFormat, item.Id), DragDropEffects.Move); }
        finally
        {
            _activeDrag = null;
            HideDropLines();
            DragFinished?.Invoke();
        }
        e.Handled = true;
    }

    private void DragHandle_MouseUp(object sender, MouseButtonEventArgs e)
    {
        ResetDragCandidate();
        e.Handled = true;
    }

    private void ResetDragCandidate()
    {
        _armedHandle?.ReleaseMouseCapture();
        _armedHandle = null;
        _armedItem = null;
    }

    private void TaskRow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TodoItem item } row)
            _rows[item.Id] = row;
    }

    private void TaskRow_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TodoItem item } row &&
            _rows.TryGetValue(item.Id, out var current) && ReferenceEquals(current, row))
            _rows.Remove(item.Id);
    }

    /// <summary>
    /// 根据可见行的中线决定插入边界，包含行间留白；得到边界右侧任务的 ID，
    /// 而非下标。指示线和 Drop 共用同一个锚点算法。
    /// </summary>
    private bool TryGetInsertion(ScrollViewer viewer, DragEventArgs e,
        out ItemsControl list, out int index, out string? beforeId)
    {
        list = ReferenceEquals(viewer, PendingScroller) ? PendingList : CompletedList;
        index = -1;
        beforeId = null;
        if (_activeDrag is null || _activeDrag.IsCompleted != ReferenceEquals(viewer, CompletedScroller) ||
            !e.Data.GetDataPresent(TaskDragFormat) ||
            e.Data.GetData(TaskDragFormat) is not string id || id != _activeDrag.Id)
            return false;

        // 列表行必须全部能定位；否则拒绝 Drop，绝不默认为末尾。
        var bounds = new List<(double Top, double Bottom)>();
        double previousTop = double.NegativeInfinity;
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (list.Items[i] is not TodoItem target ||
                !_rows.TryGetValue(target.Id, out var row) ||
                !row.IsLoaded || row.ActualHeight <= 0)
                return false;
            double top = row.TranslatePoint(new Point(0, 0), viewer).Y;
            // 刷新后尚未完成布局的行会全部落在 0，坐标不可信；
            // 必须拒绝，否则任意指针位置都会被算成“列表末尾”。
            if (!double.IsFinite(top) || top <= previousTop) return false;
            previousTop = top;
            bounds.Add((top, top + row.ActualHeight));
        }

        double pointerY = e.GetPosition(viewer).Y;
        // B 后半段、B/C 间隙与 C 前半段均命中“插入 C 前”的同一条线。
        index = bounds.Count;
        for (int i = 0; i < bounds.Count; i++)
        {
            if (pointerY < (bounds[i].Top + bounds[i].Bottom) / 2)
            {
                index = i;
                break;
            }
        }
        beforeId = index < list.Items.Count ? (list.Items[index] as TodoItem)?.Id : null;
        return true;
    }

    private void ListScroller_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (sender is not ScrollViewer viewer) return;
        if (!TryGetInsertion(viewer, e, out var list, out int index, out _))
        {
            HideDropLines();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        double y = e.GetPosition(viewer).Y;
        const double edge = 22;
        if (y < edge) viewer.LineUp();
        else if (y > viewer.ActualHeight - edge) viewer.LineDown();
        ShowDropLine(viewer, list, index);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void ListScroller_PreviewDrop(object sender, DragEventArgs e)
    {
        if (sender is not ScrollViewer viewer ||
            !TryGetInsertion(viewer, e, out _, out _, out string? beforeId))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        var item = _activeDrag!;
        _activeDrag = null; // 只接受本次拖拽的第一次 Drop，重复事件不得再次移动
        HideDropLines();
        Change(() => _repository.ReorderBefore(item.Id, item.IsCompleted, beforeId));
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void ShowDropLine(ScrollViewer viewer, ItemsControl list, int index)
    {
        HideDropLines();
        var line = ReferenceEquals(viewer, PendingScroller) ? PendingInsertionLine : CompletedInsertionLine;
        // 横线画在两行之间的实际空白处，而不是贴在下一行的边框上。
        FrameworkElement? prev = index > 0 && list.Items[index - 1] is TodoItem previous &&
            _rows.TryGetValue(previous.Id, out var beforeRow) ? beforeRow : null;
        FrameworkElement? next = index < list.Items.Count && list.Items[index] is TodoItem following &&
            _rows.TryGetValue(following.Id, out var afterRow) ? afterRow : null;
        double y;
        if (prev is not null && next is not null)
        {
            var bottom = prev.TranslatePoint(new Point(0, prev.ActualHeight), viewer).Y;
            var top = next.TranslatePoint(new Point(0, 0), viewer).Y;
            y = (bottom + top) / 2;
        }
        else if (next is not null)
            y = next.TranslatePoint(new Point(0, 0), viewer).Y + 1;
        else if (prev is not null)
            y = prev.TranslatePoint(new Point(0, prev.ActualHeight), viewer).Y - 2;
        else
            y = 2;
        line.Width = Math.Max(1, viewer.ActualWidth - SystemParameters.VerticalScrollBarWidth - 8);
        Canvas.SetLeft(line, 3);
        Canvas.SetTop(line, Math.Clamp(y, 0, Math.Max(0, viewer.ActualHeight - 3)));
        line.Visibility = Visibility.Visible;
    }

    private void HideDropLines()
    {
        PendingInsertionLine.Visibility = Visibility.Collapsed;
        CompletedInsertionLine.Visibility = Visibility.Collapsed;
    }

    private static string? TagId(object sender) => (sender as FrameworkElement)?.Tag as string;

    private void Change(Action action)
    {
        try
        {
            action();
            RefreshItems();
            TasksChanged?.Invoke();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存失败：{ex.Message}", "FloatTodo", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
