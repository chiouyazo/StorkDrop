using System.Windows;
using System.Windows.Controls;
using StorkDrop.App.ViewModels;

namespace StorkDrop.App.Views.PluginTab;

public partial class PluginTabView : UserControl
{
    public PluginTabView()
    {
        InitializeComponent();
    }

    private void OnDbPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb && pb.Tag is StepsDatabaseViewModel db)
            db.Password = pb.Password;
    }

    private void OnRdpPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb && pb.Tag is StepsPathViewModel path)
            path.RdpPassword = pb.Password;
    }
}
