using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using StorkDrop.Contracts;
using StorkDrop.Contracts.Models;

namespace StorkDrop.App.Views;

public partial class PluginPromptDialog : Window
{
    public int ChosenIndex { get; private set; } = -1;

    public Dictionary<string, string> FieldValues { get; } = new Dictionary<string, string>();

    private readonly List<(string Key, System.Func<string> Read)> _fieldReaders = [];

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

        BuildFields(prompt.Fields);

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
                CaptureFieldValues();
                ChosenIndex = index;
                DialogResult = true;
                Close();
            };

            ButtonsPanel.Items.Add(button);
        }
    }

    private void BuildFields(IReadOnlyList<PluginPromptField> fields)
    {
        foreach (PluginPromptField field in fields)
        {
            switch (field.FieldType)
            {
                case PluginFieldType.Checkbox:
                    AddCheckbox(field);
                    break;
                case PluginFieldType.MultiSelect:
                    AddMultiSelect(field);
                    break;
                case PluginFieldType.Dropdown:
                    AddDropdown(field);
                    break;
                case PluginFieldType.Password:
                    AddPassword(field);
                    break;
                default:
                    AddText(field);
                    break;
            }
        }
    }

    private void AddCheckbox(PluginPromptField field)
    {
        CheckBox box = new CheckBox
        {
            Content = field.Label,
            IsChecked = string.Equals(
                field.DefaultValue,
                "true",
                System.StringComparison.OrdinalIgnoreCase
            ),
            Margin = new Thickness(0, 0, 0, 8),
        };
        FieldsPanel.Children.Add(box);
        _fieldReaders.Add((field.Key, () => box.IsChecked == true ? "true" : "false"));
    }

    private void AddMultiSelect(PluginPromptField field)
    {
        AddLabel(field.Label);
        HashSet<string> preselected = new HashSet<string>(
            (field.DefaultValue ?? string.Empty).Split(
                ',',
                System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries
            ),
            System.StringComparer.OrdinalIgnoreCase
        );

        List<(CheckBox Box, string Value)> boxes = [];
        foreach (PluginOptionItem option in field.Options)
        {
            CheckBox box = new CheckBox
            {
                Content = option.Label,
                IsChecked = preselected.Contains(option.Value),
                Margin = new Thickness(0, 0, 0, 4),
            };
            FieldsPanel.Children.Add(box);
            boxes.Add((box, option.Value));
        }
        FieldsPanel.Children.Add(new Border { Height = 4 });

        _fieldReaders.Add(
            (
                field.Key,
                () =>
                {
                    List<string> selected = [];
                    foreach ((CheckBox box, string value) in boxes)
                    {
                        if (box.IsChecked == true)
                            selected.Add(value);
                    }
                    return string.Join(",", selected);
                }
            )
        );
    }

    private void AddDropdown(PluginPromptField field)
    {
        AddLabel(field.Label);
        ComboBox combo = new ComboBox
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(8, 6, 8, 6),
        };
        foreach (PluginOptionItem option in field.Options)
        {
            ComboBoxItem item = new ComboBoxItem { Content = option.Label, Tag = option.Value };
            combo.Items.Add(item);
            if (string.Equals(option.Value, field.DefaultValue, System.StringComparison.Ordinal))
                combo.SelectedItem = item;
        }
        if (combo.SelectedItem is null && combo.Items.Count > 0)
            combo.SelectedIndex = 0;

        FieldsPanel.Children.Add(combo);
        _fieldReaders.Add(
            (
                field.Key,
                () =>
                    combo.SelectedItem is ComboBoxItem { Tag: string value } ? value : string.Empty
            )
        );
    }

    private void AddPassword(PluginPromptField field)
    {
        AddLabel(field.Label);
        PasswordBox box = new PasswordBox
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(8, 6, 8, 6),
        };
        if (!string.IsNullOrEmpty(field.DefaultValue))
            box.Password = field.DefaultValue;
        FieldsPanel.Children.Add(box);
        _fieldReaders.Add((field.Key, () => box.Password));
    }

    private void AddText(PluginPromptField field)
    {
        AddLabel(field.Label);
        TextBox box = new TextBox
        {
            Text = field.DefaultValue ?? string.Empty,
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(8, 6, 8, 6),
        };
        FieldsPanel.Children.Add(box);
        _fieldReaders.Add((field.Key, () => box.Text));
    }

    private void AddLabel(string text) =>
        FieldsPanel.Children.Add(
            new TextBlock
            {
                Text = text,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4),
            }
        );

    private void CaptureFieldValues()
    {
        foreach ((string key, System.Func<string> read) in _fieldReaders)
            FieldValues[key] = read();
    }
}
