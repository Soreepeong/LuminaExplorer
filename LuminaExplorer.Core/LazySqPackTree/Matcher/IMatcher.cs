using System.Diagnostics;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher;

public interface IMatcher {
    public bool Matches(VirtualSqPackTree tree, VirtualFolder folder, Stopwatch stopwatch, TimeSpan timeout);

    public bool Matches(VirtualSqPackTree tree, VirtualFile file, ref VirtualFileLookup? lookup, Lazy<string> data,
        Stopwatch stopwatch, TimeSpan timeout);

    public IMatcher UnwrapIfPossible();
}
