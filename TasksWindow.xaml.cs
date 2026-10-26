using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FloatTodo.Models;
using FloatTodo.Services;

namespace FloatTodo;

public partial class TasksWindow : Window
{
    private const string TaskDragFormat = "FloatTodo.Internal.TaskId";
    private static readonly Brush DragHighlightBrush =
        new SolidColorBrush(Color.FromRgb(87, 125, 240));
    private static readonly Brush ClearBorderBrush = Brushes.Transparent;

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

    private void NewTaskBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Add_Click(sender, e); e.Handled = true; }
    }

    private void Complete_Checked(object sender, RoutedEventArgs e)
    {
        if (TagId(sender) is { } id)
            Change(() => _repository.SetCompleted(id, true));
    }

    private void Reopen_Unchecked(object sender, RoutedEventArgs e)
    {
        if (TagId(sender) is { } id)
            Change(() => _repository.SetCompleted(id, false));
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (TagId(sender) is { } id)
            Change(() => _repository.Delete(id));
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

    // 仅从每行左侧的拖拽手柄发起拖动，避免与复选框、编辑、删除按钮冲突。
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
        if (_armedHandle is null || _armedItem is null || !ReferenceEquals(sender, _armedHandle))
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ResetDragCandidate();
            return;
        }

        var pointer = e.GetPosition(this);
        if (Math.Abs(pointer.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pointer.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var handle = _armedHandle;
        var item = _armedItem;
        ResetDragCandidate();  // 开始 DoDragDrop 之前释放鼠标捕获，允许目标行接收 Drop。
        _activeDrag = item;
        try
        {
            DragDrop.DoDragDrop(handle,
                new DataObject(TaskDragFormat, item.Id), DragDropEffects.Move);
        }
        finally
        {
            _activeDrag = null;
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

    private bool TryGetDropTarget(object sender, DragEventArgs e, out Border? row, out TodoItem? target)
    {
        row = sender as Border;
        target = row?.DataContext as TodoItem;
        return _activeDrag is not null && row is not null && target is not null &&
               _activeDrag.Id != target.Id && _activeDrag.IsCompleted == target.IsCompleted &&
               e.Data.GetDataPresent(TaskDragFormat) &&
               e.Data.GetData(TaskDragFormat) is string id && id == _activeDrag.Id;
    }

    // 列表较长时，拖到可视区域上下边缘即可继续滚动寻找目标。
    private void ListScroller_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (_activeDrag is null || sender is not ScrollViewer viewer) return;
        if (ReferenceEquals(viewer, PendingScroller) && _activeDrag.IsCompleted) return;
        if (ReferenceEquals(viewer, CompletedScroller) && !_activeDrag.IsCompleted) return;

        double y = e.GetPosition(viewer).Y;
        const double edge = 26;
        if (y < edge) viewer.LineUp();
        else if (y > viewer.ActualHeight - edge) viewer.LineDown();
        // 不标记 Handled：下层任务行仍需接收 DragOver/Drop。
    }

    private void TaskRow_DragOver(object sender, DragEventArgs e)
    {
        if (!TryGetDropTarget(sender, e, out var row, out _))
        {
            if (sender is Border invalidRow) ClearDropHint(invalidRow);
            e.Effects = DragDropEffects.None;
        }
        else
        {
            // 上半部分表示插入目标前面，下半部分表示插入目标后面。
            bool insertAfter = e.GetPosition(row!).Y >= row!.ActualHeight / 2;
            row.BorderBrush = DragHighlightBrush;
            row.BorderThickness = insertAfter
                ? new Thickness(1, 1, 1, 3) : new Thickness(1, 3, 1, 1);
            e.Effects = DragDropEffects.Move;
        }
        e.Handled = true;
    }

    private void TaskRow_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border row) ClearDropHint(row);
        e.Handled = true;
    }

    private void TaskRow_Drop(object sender, DragEventArgs e)
    {
        if (sender is Border row) ClearDropHint(row);
        if (TryGetDropTarget(sender, e, out var targetRow, out var target))
        {
            bool insertAfter = e.GetPosition(targetRow!).Y >= targetRow!.ActualHeight / 2;
            string draggedId = _activeDrag!.Id;
            string targetId = target!.Id;
            Change(() => _repository.ReorderWithinGroup(draggedId, targetId, insertAfter));
            e.Effects = DragDropEffects.Move;
        }
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private static void ClearDropHint(Border row)
    {
        row.BorderBrush = ClearBorderBrush;
        row.BorderThickness = new Thickness(1);
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
            MessageBox.Show(this, $"保存失败：{ex.Message}", "FloatTodo",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
