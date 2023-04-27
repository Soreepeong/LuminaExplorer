using System.Runtime.InteropServices;
using DirectN;

namespace LuminaExplorer.Core.VirtualFileSystem.Shell;

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPropertyStore {
    void GetCount(out uint cProps);
    void GetAt([In] uint iProp, out PropertyKey pKey);
    void GetValue([In] ref PropertyKey key, [Out] PropVariant pv);
    void SetValue([In] ref PropertyKey key, [In] PropVariant propvar);
    void Commit();
}