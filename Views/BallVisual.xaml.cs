using System.Windows;
using System.Windows.Controls;

namespace FloatTodo.Views;

public partial class BallVisual : UserControl
{
    public BallVisual() => InitializeComponent();

    /// <summary>按比例缩放整个悬浮球（含边框、对勾和角标）。</summary>
    public void SetUiScale(double scale)
    {
        RootScale.ScaleX = scale;
        RootScale.ScaleY = scale;
    }

    public void ShowPendingCount(int count)
    {
        CountBadge.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
        CountText.Text = count > 99 ? "99+" : count.ToString();
    }
}
