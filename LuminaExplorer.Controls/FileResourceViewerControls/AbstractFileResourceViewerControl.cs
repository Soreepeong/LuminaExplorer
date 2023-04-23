using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Lumina.Data;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.LazySqPackTree;

namespace LuminaExplorer.Controls.FileResourceViewerControls;

public abstract class AbstractFileResourceViewerControl : Control {
    protected readonly TaskScheduler UiTaskScheduler = TaskScheduler.FromCurrentSynchronizationContext();

    public readonly MouseActivityTracker MouseActivity;

    public VirtualSqPackTree? Tree { get; private set; }

    public VirtualFile? File { get; private set; }

    public FileResource? FileResourceUntyped { get; private set; }

    protected AbstractFileResourceViewerControl() {
        MouseActivity = new(this);
    }

    protected override void Dispose(bool disposing) {
        if (disposing)
            MouseActivity.Dispose();

        base.Dispose(disposing);
    }

    public virtual void SetFile(VirtualSqPackTree tree, VirtualFile file, FileResource fileResource) {
        Tree = tree;
        File = file;
        FileResourceUntyped = fileResource;
        Text = file.Name;
    }

    public virtual void ClearFile(bool keepContentsDisplayed = false) {
        Tree = null;
        File = null;
        FileResourceUntyped = null;
        Text = "";
    }

    public override Size GetPreferredSize(Size proposedSize) => new(320, 240);

    public virtual Task<Size> GetPreferredSizeAsync(Size proposedSize) =>
        Task.FromResult(GetPreferredSize(proposedSize));

    private Rectangle GetViewportRectangleSuggestionImpl(Control? opener, Size preferredSize) {
        var screen = opener is null ? Screen.FromPoint(Cursor.Position) : Screen.FromControl(opener);

        var pos = opener?.PointToScreen(new(opener.Width / 2, opener.Height / 2)) ?? new(
            screen.WorkingArea.Left + screen.WorkingArea.Width / 2,
            screen.WorkingArea.Top + screen.WorkingArea.Height / 2);

        if (Parent is { } parent) {
            var rcParentClient = parent.RectangleToScreen(parent.ClientRectangle);

            preferredSize = new(
                Math.Min(preferredSize.Width + parent.Width - rcParentClient.Width, screen.WorkingArea.Width),
                Math.Min(preferredSize.Height + parent.Height - rcParentClient.Height, screen.WorkingArea.Height));
        } else {
            preferredSize = new(
                Math.Min(preferredSize.Width, screen.WorkingArea.Width),
                Math.Min(preferredSize.Height, screen.WorkingArea.Height));
        }

        pos = new(
            Math.Min(
                Math.Max(pos.X - preferredSize.Width / 2, screen.WorkingArea.Left),
                screen.WorkingArea.Right - preferredSize.Width),
            Math.Min(
                Math.Max(pos.Y - preferredSize.Height / 2, screen.WorkingArea.Top),
                screen.WorkingArea.Bottom - preferredSize.Height));

        return new(pos, preferredSize);
    }

    public Rectangle GetViewportRectangleSuggestion(Control? opener) =>
        GetViewportRectangleSuggestionImpl(opener, GetPreferredSize(opener?.Size ?? Size.Empty));

    public async Task<Rectangle> GetViewportRectangleSuggestionAsync(Control? opener) =>
        GetViewportRectangleSuggestionImpl(opener,await  GetPreferredSizeAsync(opener?.Size ?? Size.Empty));

    public Task RunOnUiThread(Action action, bool allowChildAttach = false) => Task.Factory.StartNew(
        action,
        default,
        allowChildAttach
            ? TaskCreationOptions.None
            : TaskCreationOptions.DenyChildAttach | TaskCreationOptions.RunContinuationsAsynchronously,
        UiTaskScheduler);

    public Task<T> RunOnUiThread<T>(Func<T> action, bool allowChildAttach = false) => Task.Factory.StartNew(
        action,
        default,
        allowChildAttach ? TaskCreationOptions.None : TaskCreationOptions.DenyChildAttach,
        UiTaskScheduler);

    public Task RunOnUiThreadAfter(Task taskBefore, Action<Task> action, bool allowChildAttach = false) =>
        taskBefore.ContinueWith(
            action,
            default,
            allowChildAttach
                ? TaskContinuationOptions.None
                : TaskContinuationOptions.DenyChildAttach | TaskContinuationOptions.RunContinuationsAsynchronously,
            UiTaskScheduler);

    public Task RunOnUiThreadAfter<T>(Task<T> taskBefore, Action<Task<T>> action, bool allowChildAttach = false) =>
        taskBefore.ContinueWith(
            action,
            default,
            allowChildAttach
                ? TaskContinuationOptions.None
                : TaskContinuationOptions.DenyChildAttach | TaskContinuationOptions.RunContinuationsAsynchronously,
            UiTaskScheduler);

    public Task RunOnUiThreadAfter<TReturn>(
        Task taskBefore,
        Func<Task, TReturn> action,
        bool allowChildAttach = false) =>
        taskBefore.ContinueWith(
            action,
            default,
            allowChildAttach
                ? TaskContinuationOptions.None
                : TaskContinuationOptions.DenyChildAttach | TaskContinuationOptions.RunContinuationsAsynchronously,
            UiTaskScheduler);

    public Task<TReturn> RunOnUiThreadAfter<T, TReturn>(
        Task<T> taskBefore,
        Func<Task<T>, TReturn> action,
        bool allowChildAttach = false) =>
        taskBefore.ContinueWith(
            action,
            default,
            allowChildAttach
                ? TaskContinuationOptions.None
                : TaskContinuationOptions.DenyChildAttach | TaskContinuationOptions.RunContinuationsAsynchronously,
            UiTaskScheduler);
}
