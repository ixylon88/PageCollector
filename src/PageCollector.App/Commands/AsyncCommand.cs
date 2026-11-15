using System.Windows.Input;

namespace PageCollector.App.Commands;
/// <summary>Prevents overlapping command executions and observes UI event-handler failures.</summary>
public sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute, Action<Exception> onError) : ICommand
{
    private bool running;

    public event EventHandler? CanExecuteChanged;

    public Task ExecutionTask { get; private set; } = Task.CompletedTask;

    public bool CanExecute(object? parameter) => !running && canExecute();

    public async void Execute(object? parameter)
    {
        await ExecuteAsync();
    }

    public Task ExecuteAsync()
    {
        if (!CanExecute(null))
        {
            return Task.CompletedTask;
        }

        ExecutionTask = ExecuteCoreAsync();

        return ExecutionTask;
    }

    public void NotifyCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ExecuteCoreAsync()
    {
        running = true;
        NotifyCanExecuteChanged();

        try
        {
            await execute();
        }
        catch (Exception exception)
        {
            onError(exception);
        }
        finally
        {
            running = false;
            NotifyCanExecuteChanged();
        }
    }
}