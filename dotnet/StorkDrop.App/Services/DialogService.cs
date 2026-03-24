using System.Windows;
using Microsoft.Win32;
using StorkDrop.App.Localization;

namespace StorkDrop.App.Services;

public sealed class DialogService
{
    public string? ShowFolderPicker(string description = "Select a folder")
    {
        OpenFolderDialog dialog = new() { Title = description };

        bool? result = dialog.ShowDialog();
        return result == true ? dialog.FolderName : null;
    }

    public string? ShowOpenFilePicker(
        string filter = "All files (*.*)|*.*",
        string title = "Open File"
    )
    {
        OpenFileDialog dialog = new() { Filter = filter, Title = title };

        bool? result = dialog.ShowDialog();
        return result == true ? dialog.FileName : null;
    }

    public string? ShowSaveFilePicker(
        string filter = "All files (*.*)|*.*",
        string title = "Save File"
    )
    {
        SaveFileDialog dialog = new() { Filter = filter, Title = title };

        bool? result = dialog.ShowDialog();
        return result == true ? dialog.FileName : null;
    }

    public bool ShowConfirmation(string message, string? title = null)
    {
        title ??= LocalizationManager.GetString("Dialog_Title_Confirm");
        MessageBoxResult result = MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question
        );
        return result == MessageBoxResult.Yes;
    }

    public void ShowInfo(string message, string? title = null)
    {
        title ??= LocalizationManager.GetString("Dialog_Title_Information");
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void ShowError(string message, string? title = null)
    {
        title ??= LocalizationManager.GetString("Dialog_Title_Error");
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
