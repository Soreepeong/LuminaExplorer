using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LuminaExplorer.Core.VirtualFileSystem.Matcher;

public interface IMatcher {
    public Task<bool> Matches(IVirtualFileSystem tree, IVirtualFolder folder, Stopwatch stopwatch, TimeSpan timeout,
        CancellationToken cancellationToken);

    public Task<bool> Matches(IVirtualFileSystem tree, IVirtualFile file, Lazy<IVirtualFileLookup> lookup,
        Task<Task<string>> data, Stopwatch stopwatch, TimeSpan timeout, CancellationToken cancellationToken);

    public IMatcher UnwrapIfPossible();
}
