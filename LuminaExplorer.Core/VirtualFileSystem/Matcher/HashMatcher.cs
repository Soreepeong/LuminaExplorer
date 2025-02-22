using System.Threading;
using System.Threading.Tasks;

namespace LuminaExplorer.Core.VirtualFileSystem.Matcher;

public class HashMatcher {
    private uint _value;

    public HashMatcher(uint value) => this._value = value;

    public Task<bool> Matches(uint? hash, CancellationToken cancellationToken) => Task.FromResult(this._value == hash);

    public override string ToString() => $"Hash({this._value:X08})";
}
