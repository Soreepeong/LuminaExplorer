using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using BrightIdeasSoftware;

namespace LuminaExplorer.Controls;

public class CoreVirtualObjectListView : VirtualObjectListView {
    private static FieldInfo? _virtualListSizeFieldInfo;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    protected override int VirtualListSize
    {
        get => base.VirtualListSize;
        set
        {
            if (value == VirtualListSize || value < 0)
                return;
            _virtualListSizeFieldInfo ??= typeof (ListView).GetField("_virtualListSize", BindingFlags.Instance | BindingFlags.NonPublic);
            _virtualListSizeFieldInfo!.SetValue((object) this, (object) value);
            if (!IsHandleCreated || DesignMode)
                return;
            NativeMethods.SetItemCount((ListView) this, value);
        }
    }

    static class NativeMethods {
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        public static void SetItemCount(ListView list, int count) => SendMessage(list.Handle, 4143, count, 2);
    }
}
