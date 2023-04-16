namespace LuminaExplorer.Core.Util; 

public interface IReferenceCounted {
    public void AddRef();

    public void DecRef();

    public static void DecRef<T>(ref T? man) where T : class, IReferenceCounted {
        man?.DecRef();
        man = null;
    }
}
