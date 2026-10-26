using System.Windows;
using System.Windows.Controls;

namespace FloatTodo.Views;

public partial class BallVisual : UserControl
{
    public BallVisual() => InitializeComponent();

    public void ShowPendingCount(int count)
    {
        CountBadge.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
        CountText.Text = count > 99 ? "99+" : count.ToString();
    }
}
