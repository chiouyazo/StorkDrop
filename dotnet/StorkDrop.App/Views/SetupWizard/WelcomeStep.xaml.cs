using System.Windows.Controls;
using StorkDrop.App.Localization;

namespace StorkDrop.App.Views.SetupWizard;

public partial class WelcomeStep : UserControl
{
    public WelcomeStep()
    {
        InitializeComponent();

        LanguageSelector.ItemsSource = LocalizationManager.AvailableLanguages;
        LanguageSelector.SelectedItem = LocalizationManager.Language;
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageSelector.SelectedItem is string language)
        {
            LocalizationManager.Language = language;
        }
    }
}
