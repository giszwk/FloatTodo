using System;
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
        ResetDragCandidate();
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

    /// <summary>
    /// 以列表整体（包括行间空隙）计算插入边界：
    /// B 下半区 / B-C 的间隙 / C 上半区都指向 B-C 之间的那条线。
    /// </summary>
    private bool TryGetInsertion(ScrollViewer viewer, DragEventArgs e, out ItemsControl list, out int index)
    {
        list = ReferenceEquals(viewer, PendingScroller) ? PendingList : CompletedList;
        index = -1;
        if (_activeDrag is null || _activeDrag.IsCompleted != ReferenceEquals(viewer, CompletedScroller) ||
            !e.Data.GetDataPresent(TaskDragFormat) ||
            e.Data.GetData(TaskDragFormat) is not string id || id != _activeDrag.Id)
            return false;
        var pointerY = e.GetPosition(viewer).Y;
        index = list.Items.Count;
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement row) continue;
            double top = row.TranslatePoint(new Point(0, 0), viewer).Y;
            if (pointerY < top + row.ActualHeight / 2)
            {
                index = i;
                break;
            }
        }
        return true;
    }

    private void ListScroller_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (sender is not ScrollViewer viewer) return;
        if (!TryGetInsertion(viewer, e, out var list, out int index))
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
            !TryGetInsertion(viewer, e, out _, out int index))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        var item = _activeDrag!;
        HideDropLines();
        Change(() => _repository.ReorderAtIndex(item.Id, item.IsCompleted, index));
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void ShowDropLine(ScrollViewer viewer, ItemsControl list, int index)
    {
        HideDropLines();
        var line = ReferenceEquals(viewer, PendingScroller) ? PendingInsertionLine : CompletedInsertionLine;
        double y = 1;
        if (list.Items.Count > 0)
        {
            int itemIndex = Math.Min(index, list.Items.Count - 1);
            if (list.ItemContainerGenerator.ContainerFromIndex(itemIndex) is FrameworkElement row)
            {
                double top = row.TranslatePoint(new Point(0, 0), viewer).Y;
                y = index >= list.Items.Count ? top + row.ActualHeight - 4 : top - 4;
            }
        }
        line.Width = Math.Max(1, viewer.ViewportWidth - 17);
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
