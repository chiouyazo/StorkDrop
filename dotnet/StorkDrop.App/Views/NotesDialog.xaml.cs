using System.Windows;
using StorkDrop.App.Localization;

namespace StorkDrop.App.Views;

/// <summary>
/// Free-text, multi-line notes editor for a single installed instance. Returns the edited text; an empty
/// result clears the note.
/// </summary>
public partial class NotesDialog : Window
{
    /// <summary>The edited note text, or null if cancelled.</summary>
    public string? Notes { get; private set; }

    public NotesDialog(string displayName, string? notes)
    {
        InitializeComponent();

        MessageText.Text = LocalizationManager
            .GetString("Notes_Message")
            .Replace("{0}", displayName);
        NotesTextBox.Text = notes ?? string.Empty;

        Loaded += (_, _) =>
        {
            NotesTextBox.Focus();
            NotesTextBox.CaretIndex = NotesTextBox.Text.Length;
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Notes = NotesTextBox.Text;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
