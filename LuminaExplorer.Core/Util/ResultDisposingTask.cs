using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace LuminaExplorer.Core.Util;

public sealed class ResultDisposingTask<T> : IDisposable, IAsyncDisposable {
    public readonly Task<T> Task;

    public ResultDisposingTask(Task<T> task)
    {
        this.Task = task;
    }

    public bool IsCompletedSuccessfully => this.Task.IsCompletedSuccessfully;
    public bool IsCompleted => this.Task.IsCompleted;
    public bool IsCanceled => this.Task.IsCanceled;
    public bool IsFaulted => this.Task.IsFaulted;
    public TaskStatus Status => this.Task.Status;
    public T Result => this.Task.Result;

    public ConfiguredTaskAwaitable<T> ConfigureAwait(bool continueOnCapturedContext) =>
        this.Task.ConfigureAwait(continueOnCapturedContext);

    public void Dispose()
    {
        this.Task.ContinueWith(
            result => {
                if (!result.IsCompletedSuccessfully)
                    return;
                if (result.Result is IDisposable disposable)
                    disposable.Dispose();
            });
    }

    public ValueTask DisposeAsync() => new(
        this.Task.ContinueWith(
            result => {
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
