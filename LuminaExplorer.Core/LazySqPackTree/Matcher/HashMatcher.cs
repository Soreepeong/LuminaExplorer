namespace LuminaExplorer.Core.LazySqPackTree.Matcher;

public class HashMatcher {
    private uint _value;

    public HashMatcher(uint value) => _value = value;

    public Task<bool> Matches(uint hash, CancellationToken cancellationToken) => Task.FromResult(_value == hash);

    public override string ToString() => $"Hash({_value:X08})";
}
