using System;
using System.Windows.Forms;

namespace LuminaExplorer.Controls.Util;

public static class ControlExtensions {
    private const int WmSetRedraw = 0x000B;

    public static ScopedDisableRedraw DisableRedrawScoped(this Control control) => new(control);

    public static void DisableRedraw(this Control control) {
        var msgSuspendUpdate = Message.Create(control.Handle, WmSetRedraw, 0, 0);
        var window = NativeWindow.FromHandle(control.Handle);
        window?.DefWndProc(ref msgSuspendUpdate);
    }

    public static void EnableRedraw(this Control control) {
        var msgResumeUpdate = Message.Create(control.Handle, WmSetRedraw, 1, 0);
        var window = NativeWindow.FromHandle(control.Handle);
        window?.DefWndProc(ref msgResumeUpdate);
        control.Refresh();
    }

    public sealed class ScopedDisableRedraw : IDisposable {
        private readonly Control _c;

        public ScopedDisableRedraw(Control c) {
            _c = c;
            c.DisableRedraw();
        }

        public void Dispose() {
            _c.EnableRedraw();
        }
    }
}
