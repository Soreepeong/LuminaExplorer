namespace LuminaExplorer.Core.Util;

public static class TaskExtensions {
    public static Task AsStarted(this Task t) {
        if (t.Status is TaskStatus.Created) {
            try {
                t.Start();
            } catch (InvalidOperationException) {
                // ignore
            }
        }

        return t;
    }

    public static Task<T> AsStarted<T>(this Task<T> t) {
        if (t.Status is TaskStatus.Created) {
            try {
                t.Start();
            } catch (InvalidOperationException) {
                // ignore
            }
        }

        return t;
    }
}
