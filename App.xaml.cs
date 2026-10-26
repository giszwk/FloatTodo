using System;
using System.Threading;
using System.Windows;
using FloatTodo.Services;

namespace FloatTodo;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(initiallyOwned: true,
            name: @"Local\FloatTodo.Desktop.SingleInstance", createdNew: out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("FloatTodo 已经在运行，请检查桌面或系统托盘。", "FloatTodo",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        try
        {
            var tasks = new TaskRepository();
            var settings = new SettingsStore();
            var main = new MainWindow(tasks, settings);
            MainWindow = main;
            main.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动失败：{ex.Message}\n\n请检查应用数据目录是否可写。",
                "FloatTodo", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_singleInstance is not null)
        {
            try { _singleInstance.ReleaseMutex(); }
            catch (ApplicationException) { /* 当前进程未持有该锁 */ }
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }
}
