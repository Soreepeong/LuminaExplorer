using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher;

public interface IMatcher {
    public Task<bool> Matches(VirtualSqPackTree tree, VirtualFolder folder, Stopwatch stopwatch, TimeSpan timeout,
        CancellationToken cancellationToken);

    public Task<bool> Matches(VirtualSqPackTree tree, VirtualFile file, Lazy<VirtualFileLookup> lookup,
        Task<Task<string>> data, Stopwatch stopwatch, TimeSpan timeout, CancellationToken cancellationToken);

    public IMatcher UnwrapIfPossible();
}
