namespace PageCollector.App.Services;

public interface IDesktopServices
{
    string? ChooseOutputFolder();

    string? ChooseBrowser();

    string? ChooseValuesFile();

    void OpenPath(string path);

    void ShowError(string message);

    void Invoke(Action action);
}