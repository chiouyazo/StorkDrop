using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace StorkDrop.Demo.Ui;

internal sealed class DemoToastWindow : Window
{
    private static readonly List<DemoToastWindow> Open = [];

    private DemoToastWindow(string kind, string title, string message)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Width = 360;
        SizeToContent = SizeToContent.Height;
        Background = Brushes.White;
        BorderBrush = kind == "Error" ? Brushes.Firebrick : Brushes.SteelBlue;
        BorderThickness = new Thickness(2);

        StackPanel panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(
            new TextBlock
            {
                Text = $"{kind}: {title}",
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            }
        );
        panel.Children.Add(
            new TextBlock
            {
                Text = message,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            }
        );
        Content = panel;

        DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Close();
        };
        Loaded += (_, _) => timer.Start();
        Closed += (_, _) =>
        {
            Open.Remove(this);
            Arrange();
        };
    }

    public static void ShowToast(string kind, string title, string message)
    {
        DemoToastWindow toast = new DemoToastWindow(kind, title, message);
        Open.Add(toast);
        toast.Left = SystemParameters.WorkArea.Right - 380;
        toast.Top = SystemParameters.WorkArea.Bottom - 120;
        toast.Show();
        Arrange();
    }

    private static void Arrange()
    {
        double bottom = SystemParameters.WorkArea.Bottom - 10;
        foreach (DemoToastWindow toast in Open.AsEnumerable().Reverse())
        {
            double height = toast.ActualHeight > 0 ? toast.ActualHeight : 100;
            toast.Top = bottom - height;
            bottom -= height + 8;
        }
    }
}
