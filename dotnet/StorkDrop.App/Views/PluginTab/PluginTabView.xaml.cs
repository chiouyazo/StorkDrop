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

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb && pb.Tag is PluginConfigFieldViewModel field)
            field.Value = pb.Password;
    }

    private void OnCheckBoxChanged(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.Tag is PluginConfigFieldViewModel field)
            field.Value = cb.IsChecked == true ? "true" : "false";
    }

    private void OnCheckBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.Tag is PluginConfigFieldViewModel field)
            cb.IsChecked = string.Equals(field.Value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private void OnRemoveGroupInstance(object sender, RoutedEventArgs e)
    {
        if (
            sender is FrameworkElement { DataContext: GroupInstanceViewModel instance }
            && DataContext is PluginTabViewModel vm
        )
        {
            foreach (PluginSettingsSectionViewModel section in vm.Sections)
            {
                foreach (GroupFieldViewModel group in section.GroupFields)
                {
                    if (group.Instances.Contains(instance))
                    {
                        group.Instances.Remove(instance);
                        return;
                    }
                }
            }
        }
    }
}
