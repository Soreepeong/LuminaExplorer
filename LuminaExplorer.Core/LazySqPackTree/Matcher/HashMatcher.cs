namespace LuminaExplorer.Core.LazySqPackTree.Matcher;

public class HashMatcher {
    private uint _value;

    public HashMatcher(uint value) => _value = value;

    public bool Matches(uint hash) => _value == hash;

    public override string ToString() => $"Hash({_value:X08})";
}
