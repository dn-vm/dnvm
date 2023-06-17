
using System.Diagnostics;

public sealed class TaskScope
{
    private readonly CancellationTokenSource _cts = new();
    private List<Task>? _tasks = new();

    public CancellationToken CancellationToken => _cts.Token;

    private TaskScope() { }

    public async static ValueTask With(Func<TaskScope, ValueTask> action, Action<OperationCanceledException>? onCanceled = null)
    {
        var scope = new TaskScope();
        List<Task> finalTasks = scope._tasks!;
        try
        {
            await action(scope);
            scope._tasks = null;
            foreach (var task in finalTasks)
            {
                if (!task.IsCompleted)
                {
                    scope.Cancel();
                    break;
                }
            }
        }
        catch (OperationCanceledException e) when (e.CancellationToken == scope.CancellationToken)
        {
            onCanceled?.Invoke(e);
        }
        await Task.WhenAll(finalTasks);
    }

    private void Cancel() => _cts.Cancel();

    public Task Run(Func<Task> action)
    {
        if (_tasks is null)
        {
            throw new InvalidOperationException("TaskScope has ended.");
        }

        var task = Task.Run(async () =>
        {
            try
            {
                await action();
            }
            catch (OperationCanceledException e) when (e.CancellationToken == _cts.Token)
            {
                // Swallow cancellation for this scope
            }
            catch
            {
                // Cancel all other tasks when an unhandled exception occurs and rethrow
                _cts.Cancel();
                throw;
            }
        }, _cts.Token);
        _tasks.Add(task);
        return task;
    }
}