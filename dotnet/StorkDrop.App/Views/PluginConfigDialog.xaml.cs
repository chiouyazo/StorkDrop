using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using StorkDrop.App.ViewModels;
using StorkDrop.Contracts;

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
            OpenFileDialog dialog = new() { Title = field.Label };
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
            OpenFolderDialog dialog = new() { Title = field.Label };
            if (dialog.ShowDialog() == true)
            {
                field.Value = dialog.FolderName;
            }
        }
    }

    private void MultiSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.Tag is PluginConfigFieldViewModel field)
        {
            List<string> selectedValues = [];
            foreach (object item in listBox.SelectedItems)
            {
                if (item is PluginOptionItem option)
                {
                    selectedValues.Add(option.Value);
                }
            }
            field.Value = string.Join(",", selectedValues);
        }
    }
}
