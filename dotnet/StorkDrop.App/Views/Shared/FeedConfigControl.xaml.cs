using System.Windows.Controls;

namespace StorkDrop.App.Views.Shared;

public partial class FeedConfigControl : UserControl
{
    public FeedConfigControl()
    {
        InitializeComponent();
        PasswordBox.PasswordChanged += (s, _) =>
        {
            if (DataContext is ViewModels.SetupWizardViewModel vm)
            {
                vm.Password = PasswordBox.Password;
            }
        };
    }
}
