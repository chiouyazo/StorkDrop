using System.Windows;
using System.Windows.Controls;
using StorkDrop.App.Localization;
using StorkDrop.Contracts.Models;

namespace StorkDrop.App.Views;

public partial class SelectInstanceDialog : Window
{
    /// <summary>The instance the user chose, or null if cancelled.</summary>
    public InstalledProduct? SelectedInstance { get; private set; }

    public SelectInstanceDialog(
        string productTitle,
        string referencedProductId,
        IReadOnlyList<InstalledProduct> instances
    )
    {
        InitializeComponent();

        MessageText.Text = LocalizationManager
            .GetString("SelectInstance_Message")
            .Replace("{0}", productTitle)
            .Replace("{1}", referencedProductId);

        InstanceBox.ItemsSource = instances;
        InstanceBox.SelectedItem = instances.FirstOrDefault();
        UpdateApplyState();
    }

    private void InstanceBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateApplyState();

    private void UpdateApplyState() =>
        ApplyButton.IsEnabled = InstanceBox.SelectedItem is InstalledProduct;

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (InstanceBox.SelectedItem is InstalledProduct instance)
        {
            SelectedInstance = instance;
            DialogResult = true;
            Close();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
