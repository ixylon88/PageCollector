using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace PageCollector.App.Services;

public sealed class WindowsDesktopServices(Window owner) : IDesktopServices
{
    public string? ChooseOutputFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Ausgabeordner auswählen"
        };

        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }

    public string? ChooseBrowser()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Edge oder Chrome auswählen",
            Filter = "Browser (*.exe)|*.exe"
        };

        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? ChooseValuesFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Suchwerte laden (UTF-8, ein Wert pro Zeile)",
            Filter = "Textdateien (*.txt)|*.txt|Alle Dateien|*.*"
        };

        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public void OpenPath(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void ShowError(string message)
    {
        MessageBox.Show(owner, message, "PageCollector", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void Invoke(Action action)
    {
        owner.Dispatcher.Invoke(action);
    }
}