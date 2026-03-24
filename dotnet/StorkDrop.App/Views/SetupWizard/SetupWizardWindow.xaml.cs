using System.Windows;
using StorkDrop.App.ViewModels;

namespace StorkDrop.App.Views.SetupWizard;

public partial class SetupWizardWindow : Window
{
    private readonly SetupWizardViewModel _viewModel;

    public SetupWizardWindow(SetupWizardViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        NextButton.Click += OnNextClick;
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
