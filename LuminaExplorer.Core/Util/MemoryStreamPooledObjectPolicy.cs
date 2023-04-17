using Microsoft.Extensions.ObjectPool;

namespace LuminaExplorer.Core.Util;

public class MemoryStreamPooledObjectPolicy : PooledObjectPolicy<MemoryStream> {
    public int InitialCapacity { get; set; } = 100;

    public int MaximumRetainedCapacity { get; set; } = 4 * 1024;

    public override MemoryStream Create() => new(InitialCapacity);

    public override bool Return(MemoryStream obj) {
        if (obj.Capacity > MaximumRetainedCapacity) {
            // Too big. Discard this one.
            return false;
        }

        obj.Position = 0;
        obj.SetLength(0);
        return true;
    }
}
