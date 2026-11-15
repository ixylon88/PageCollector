using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PageCollector.App;

namespace PageCollector.App.Tests;

[TestClass]
public sealed class MainWindowTests : ViewModelWorkspace
{
    [STATestMethod]
    public void WindowBindsSettingsCommandsAndManualVisibilityWithoutErrors()
    {
        using var listener = new BindingErrorListener();
        var source = PresentationTraceSources.DataBindingSource;
        var previousLevel = source.Switch.Level;
        source.Switch.Level = SourceLevels.Error;
        source.Listeners.Add(listener);
        var window = new MainWindow(ViewModel);

        try
        {
            window.Show();
            FlushBindings(window);
            var values = (TextBox)window.FindName("ValuesBox");
            var start = (Button)window.FindName("StartButton");
            var accept = (Button)window.FindName("AcceptButton");
            var prefix = (TextBox)window.FindName("PrefixBox");

            Assert.AreEqual("123456", values.Text);
            Assert.AreSame(ViewModel.StartCommand, start.Command);
            Assert.AreEqual(Visibility.Collapsed, accept.Visibility);
            values.Text = "654321";
            ViewModel.Automatic = false;
            ((TabControl)window.FindName("InputsPanel")).SelectedIndex = 2;
            FlushBindings(window);
            prefix.Text = "KB";
            FlushBindings(window);

            Assert.AreEqual("654321", ViewModel.Values);
            Assert.AreEqual("KB", ViewModel.Prefix);
            Assert.AreEqual(Visibility.Visible, accept.Visibility);
            Assert.IsFalse(accept.IsEnabled);
            Assert.IsTrue(start.IsEnabled);
            Assert.AreEqual("", listener.Errors.ToString());
        }
        finally
        {
            window.Close();
            source.Listeners.Remove(listener);
            source.Switch.Level = previousLevel;
        }
    }

    private static void FlushBindings(Window window)
    {
        window.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() =>
        {
        }, DispatcherPriority.ApplicationIdle);
    }

    private sealed class BindingErrorListener : TraceListener
    {
        public StringBuilder Errors { get; } = new();

        public override void Write(string? message) => Errors.Append(message);

        public override void WriteLine(string? message) => Errors.AppendLine(message);
    }
}