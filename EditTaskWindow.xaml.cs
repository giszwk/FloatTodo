using System.Windows;
using System.Windows.Input;

namespace FloatTodo;

public partial class EditTaskWindow : Window
{
    public string UpdatedTitle => TitleBox.Text.Trim();

    public EditTaskWindow(string originalTitle)
    {
        InitializeComponent();
        TitleBox.Text = originalTitle;
        Loaded += (_, _) => { TitleBox.Focus(); TitleBox.SelectAll(); };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (UpdatedTitle.Length == 0)
        {
            MessageBox.Show(this, "任务内容不能为空。", "提示", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }

    private void TitleBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Save_Click(sender, e); e.Handled = true; }
    }
}
