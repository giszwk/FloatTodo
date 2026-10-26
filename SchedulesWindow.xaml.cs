using System;
using System.Windows;
using FloatTodo.Services;

namespace FloatTodo;

public partial class SchedulesWindow : Window
{
    private readonly TaskRepository _repository;
    public SchedulesWindow(TaskRepository repository)
    {
        InitializeComponent();
        _repository = repository;
        RefreshItems();
    }
    private void RefreshItems() => SchedulesList.ItemsSource = _repository.GetSchedules();

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id }) return;
        try
        {
            _repository.StopSchedule(id);
            RefreshItems();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "停止失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
