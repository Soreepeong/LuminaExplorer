namespace LuminaExplorer.Core.LazySqPackTree.Matcher; 

public interface IMatcher {
    void ParseQuery(Span<uint> span, ref int i, uint[] validTerminators);
    bool IsEmpty();
}
