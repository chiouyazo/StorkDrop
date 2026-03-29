using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using StorkDrop.App.ViewModels;

namespace StorkDrop.App.Views;

public partial class PluginConfigDialog : Window
{
    public PluginConfigDialog()
    {
        InitializeComponent();
    }

    public Dictionary<string, string>? ResultValues { get; private set; }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PluginConfigDialogViewModel viewModel)
        {
            if (viewModel.Validate())
            {
                ResultValues = viewModel.GetValues();
                DialogResult = true;
                Close();
            }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (
            sender is PasswordBox passwordBox
            && passwordBox.Tag is PluginConfigFieldViewModel field
        )
        {
            field.Value = passwordBox.Password;
        }
    }

    private void CheckBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.Tag is PluginConfigFieldViewModel field)
        {
            checkBox.IsChecked = string.Equals(
                field.Value,
                "true",
                StringComparison.OrdinalIgnoreCase
            );
        }
    }

    private void CheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.Tag is PluginConfigFieldViewModel field)
        {
            field.Value = checkBox.IsChecked == true ? "true" : "false";
        }
    }

    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is PluginConfigFieldViewModel field)
        {
            OpenFileDialog dialog = new OpenFileDialog { Title = field.Label };
            if (dialog.ShowDialog() == true)
            {
                field.Value = dialog.FileName;
            }
        }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is PluginConfigFieldViewModel field)
        {
            OpenFolderDialog dialog = new OpenFolderDialog { Title = field.Label };
            if (dialog.ShowDialog() == true)
            {
                field.Value = dialog.FolderName;
            }
        }
    }

    private void MultiSelect_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void MultiSelectCheckBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.Tag is string value)
        {
            ItemsControl? itemsControl = FindParent<ItemsControl>(cb);
            if (itemsControl?.Tag is PluginConfigFieldViewModel field)
            {
                HashSet<string> selected = new HashSet<string>(
                    (field.Value ?? "").Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    ),
                    StringComparer.OrdinalIgnoreCase
                );
                cb.IsChecked = selected.Contains(value);
            }
        }
    }

    private void MultiSelectCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.Tag is string value)
        {
            ItemsControl? itemsControl = FindParent<ItemsControl>(cb);
            if (itemsControl?.Tag is PluginConfigFieldViewModel field)
            {
                HashSet<string> selected = new HashSet<string>(
                    (field.Value ?? "").Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    ),
                    StringComparer.OrdinalIgnoreCase
                );

                if (cb.IsChecked == true)
                    selected.Add(value);
                else
                    selected.Remove(value);

                field.Value = string.Join(",", selected);
            }
        }
    }

    private static T? FindParent<T>(System.Windows.DependencyObject child)
        where T : System.Windows.DependencyObject
    {
        System.Windows.DependencyObject? parent = System.Windows.Media.VisualTreeHelper.GetParent(
            child
        );
        while (parent is not null)
        {
            if (parent is T found)
                return found;
            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
        }
        return null;
    }
}
