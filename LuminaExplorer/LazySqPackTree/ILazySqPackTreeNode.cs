namespace LuminaExplorer.LazySqPackTree; 

public interface ILazySqPackTreeNode {
    bool IsResolved();
    void Resolve(Action<VirtualFolder> onCompleteCallback);
}
