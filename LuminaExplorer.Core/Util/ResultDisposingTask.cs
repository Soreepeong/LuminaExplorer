using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace LuminaExplorer.Core.Util;

public sealed class ResultDisposingTask<T> : IDisposable, IAsyncDisposable {
    public readonly Task<T> Task;

    public ResultDisposingTask(Task<T> task) {
        Task = task;
    }

    public bool IsCompletedSuccessfully => Task.IsCompletedSuccessfully;
    public bool IsCompleted => Task.IsCompleted;
    public bool IsCanceled => Task.IsCanceled;
    public bool IsFaulted => Task.IsFaulted;
    public TaskStatus Status => Task.Status;
    public T Result => Task.Result;

    public ConfiguredTaskAwaitable<T> ConfigureAwait(bool continueOnCapturedContext) =>
        Task.ConfigureAwait(continueOnCapturedContext);

    public void Dispose() {
        Task.ContinueWith(result => {
            if (!result.IsCompletedSuccessfully)
                return;
            if (result.Result is IDisposable disposable)
                disposable.Dispose();
        });
    }

    public ValueTask DisposeAsync() => new(Task.ContinueWith(result => {
        if (result.IsCompletedSuccessfully) {
            switch (result.Result) {
                case IAsyncDisposable asyncDisposable:
                    return asyncDisposable.DisposeAsync();
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }

        return ValueTask.CompletedTask;
    }));
}
