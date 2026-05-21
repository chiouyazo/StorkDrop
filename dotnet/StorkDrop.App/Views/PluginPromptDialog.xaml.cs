using System.Windows;
using System.Windows.Controls;
using StorkDrop.Contracts.Models;

namespace StorkDrop.App.Views;

public partial class PluginPromptDialog : Window
{
    public int ChosenIndex { get; private set; } = -1;

    public PluginPromptDialog(PluginPrompt prompt)
    {
        InitializeComponent();

        Title = prompt.Title;
        MessageText.Text = prompt.Message;

        if (!string.IsNullOrEmpty(prompt.Detail))
        {
            DetailLabel.Visibility = Visibility.Visible;
            DetailBorder.Visibility = Visibility.Visible;
            DetailText.Text = prompt.Detail;
        }

        for (int i = 0; i < prompt.Options.Count; i++)
        {
            int index = i;
            Button button = new Button
            {
                Content = prompt.Options[i],
                Padding = new Thickness(16, 8, 16, 8),
                Margin = new Thickness(i > 0 ? 8 : 0, 0, 0, 0),
            };

            if (i == prompt.DefaultOptionIndex)
            {
                button.FontWeight = FontWeights.SemiBold;
                button.Background = System.Windows.Media.Brushes.White;
                button.BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xC8, 0x10, 0x2E)
                );
                button.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xC8, 0x10, 0x2E)
                );
                button.BorderThickness = new Thickness(2);
            }

            button.Click += (_, _) =>
            {
                ChosenIndex = index;
                DialogResult = true;
                Close();
            };

            ButtonsPanel.Items.Add(button);
        }
    }
}
