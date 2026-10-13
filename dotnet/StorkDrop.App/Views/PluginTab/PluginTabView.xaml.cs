using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

    private void OnPasswordLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb && pb.Tag is PluginConfigFieldViewModel field)
            pb.Password = field.Value ?? string.Empty;
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

    private async void OnPluginButton(object sender, RoutedEventArgs e)
    {
        if (
            sender is not Button { Tag: PluginConfigFieldViewModel field }
            || DataContext is not PluginTabViewModel vm
        )
            return;

        GroupInstanceViewModel? instance = FindAncestorDataContext<GroupInstanceViewModel>(
            (DependencyObject)sender
        );
        await vm.HandleButtonClickAsync(field, instance);
    }

    // A button inside a group instance is nested under that instance's DataContext; walk up to find it so
    // the click can be scoped to the row it was pressed in (null for a top-level section button).
    private static T? FindAncestorDataContext<T>(DependencyObject start)
        where T : class
    {
        DependencyObject? current = start;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: T match })
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
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
