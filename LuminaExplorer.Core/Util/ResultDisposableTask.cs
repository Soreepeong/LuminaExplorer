namespace LuminaExplorer.Core.Util;

public sealed class ResultDisposableTask<T> : IDisposable where T : IDisposable? {
    public readonly Task<T> Task;

    public ResultDisposableTask(Task<T> task) {
        Task = task;
    }

    public void Dispose() {
        Task.ContinueWith(result => {
            if (result.IsCompletedSuccessfully)
                result.Result?.Dispose();
        });
    }
}
