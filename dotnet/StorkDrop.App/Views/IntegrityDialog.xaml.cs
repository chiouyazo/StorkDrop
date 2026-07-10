using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using StorkDrop.App.Localization;
using StorkDrop.App.ViewModels;
using StorkDrop.Contracts.Models;

namespace StorkDrop.App.Views;

public partial class IntegrityDialog : Window
{
    private readonly ObservableCollection<IntegrityRepairItem> _items;

    public IntegrityDialog(string productTitle, IReadOnlyList<FileIntegrityEntry> problems)
    {
        InitializeComponent();

        HeaderText.Text = LocalizationManager
            .GetString("Integrity_ProblemsFound")
            .Replace("{0}", problems.Count.ToString())
            .Replace("{1}", productTitle);

        _items = new ObservableCollection<IntegrityRepairItem>(
            problems.Select(p => new IntegrityRepairItem
            {
                Path = p.Path,
                StatusText = StatusText(p.Status),
            })
        );
        FilesList.ItemsSource = _items;
    }

    /// <summary>The relative paths the user chose to repair. Valid after the dialog returns true.</summary>
    public IReadOnlyList<string> SelectedPaths { get; private set; } = [];

    private static string StatusText(FileIntegrityStatus status) =>
        status switch
        {
            FileIntegrityStatus.Missing => LocalizationManager.GetString("Integrity_StatusMissing"),
            FileIntegrityStatus.Modified => LocalizationManager.GetString(
                "Integrity_StatusModified"
            ),
            _ => string.Empty,
        };

    private void OnRepairClick(object sender, RoutedEventArgs e)
    {
        SelectedPaths = _items.Where(i => i.IsSelected).Select(i => i.Path).ToList();
        DialogResult = true;
        Close();
    }
}
