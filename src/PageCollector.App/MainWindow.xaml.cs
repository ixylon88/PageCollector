using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using PageCollector.App.Services;
using PageCollector.App.ViewModels;
using PageCollector.Core;

namespace PageCollector.App;

public partial class MainWindow : Window
{
    private bool closeRequested;

    public MainWindow()
    {
        InitializeComponent();
        var dataDirectory = EngineInstaller.DataDirectory;
        SetViewModel(new MainViewModel(new WindowsDesktopServices(this), new SingleFileCaptureService(), new JsonSettingsStore(Path.Combine(dataDirectory, "settings.json")), dataDirectory));
    }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        SetViewModel(viewModel);
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void SetViewModel(MainViewModel viewModel)
    {
        DataContext = viewModel;
        viewModel.Results.CollectionChanged += Results_CollectionChanged;
    }

    private void Results_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is { Count: > 0 })
        {
            ResultsGrid.ScrollIntoView(e.NewItems[^1]);
        }
    }

    private void Results_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsGrid.SelectedItem is ResultRow row)
        {
            ViewModel.OpenResult(row);
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (ViewModel.IsBusy)
        {
            e.Cancel = true;

            if (closeRequested)
            {
                return;
            }

            closeRequested = true;
            await ViewModel.StopAsync();
            Close();

            return;
        }

        ViewModel.SaveSettings();
        ViewModel.Results.CollectionChanged -= Results_CollectionChanged;
        ViewModel.Dispose();
    }
}