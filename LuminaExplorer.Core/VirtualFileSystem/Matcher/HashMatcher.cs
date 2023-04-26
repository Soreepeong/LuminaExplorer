using System.Threading;
using System.Threading.Tasks;

namespace LuminaExplorer.Core.VirtualFileSystem.Matcher;

public class HashMatcher {
    private uint _value;

    public HashMatcher(uint value) => _value = value;

    public Task<bool> Matches(uint? hash, CancellationToken cancellationToken) => Task.FromResult(_value == hash);

    public override string ToString() => $"Hash({_value:X08})";
}
