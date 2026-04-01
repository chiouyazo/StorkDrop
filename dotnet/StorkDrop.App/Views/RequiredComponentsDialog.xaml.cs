using System.Windows;
using StorkDrop.App.Localization;

namespace StorkDrop.App.Views;

public partial class RequiredComponentsDialog : Window
{
    public RequiredComponentsDialog(string productTitle, IReadOnlyList<string> missingProductIds)
    {
        InitializeComponent();
        MessageText.Text = LocalizationManager
            .GetString("RequiredComponents_Message")
            .Replace("{0}", productTitle);
        MissingList.ItemsSource = missingProductIds;
    }

    private void Proceed_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
