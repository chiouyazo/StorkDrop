using System.Windows;
using StorkDrop.App.ViewModels;
using StorkDrop.Contracts.Services;

namespace StorkDrop.App.Views.SetupWizard;

public partial class SetupWizardWindow : Window
{
    private readonly SetupWizardViewModel _viewModel;

    public SetupWizardWindow(SetupWizardViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        ApplyBranding();

        NextButton.Click += OnNextClick;
    }

    private void ApplyBranding()
    {
        BrandingInfo branding = Branding.Current;
        if (!string.IsNullOrWhiteSpace(branding.DisplayName))
            Title = $"{branding.WindowTitle} - Setup";
    }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CanFinish)
        {
            await _viewModel.FinishCommand.ExecuteAsync(null);
            DialogResult = true;
            Close();
        }
        else
        {
            _viewModel.GoNextCommand.Execute(null);
        }
    }
}
