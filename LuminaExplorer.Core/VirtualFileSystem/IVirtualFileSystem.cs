using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace LuminaExplorer.Core.VirtualFileSystem;

public interface IVirtualFileSystem : IDisposable {
    public event FolderChangedDelegate? FolderChanged;
    
    public event FileChangedDelegate? FileChanged;

    public IVirtualFolder RootFolder{ get; }

    public IVirtualFileLookup GetLookup(IVirtualFile file);

    public Task<IVirtualFolder> AsFoldersResolved(params string[] pathComponents);
    public Task<IVirtualFolder> AsFoldersResolved(IVirtualFolder folder);
    public Task<IVirtualFolder> AsFileNamesResolved(IVirtualFolder folder);
    public void SuggestFullPath(string name);
    
    public string NormalizePath(params string[] pathComponents);

    public string GetFullPath(IVirtualFolder folder);
    public string GetFullPath(IVirtualFile file);
    public uint? GetFullPathHash(IVirtualFile file);
    public IVirtualFolder[] GetTreeFromRoot(IVirtualFolder folder);
    public bool HasNoSubfolder(IVirtualFolder folder);
    public int GetKnownFolderCount(IVirtualFolder folder);
    public List<IVirtualFile> GetFiles(IVirtualFolder folder);
    public List<IVirtualFolder> GetFolders(IVirtualFolder folder);

    public class SearchProgress {
        public readonly Stopwatch Stopwatch = new();

        public SearchProgress(object lastObject) {
            Total = 1;
            LastObject = lastObject;
        }

        public long Total { get; internal set; }
        public long Progress { get; internal set; }
        public object LastObject { get; internal set; }
        public bool Completed { get; internal set; }
    }

    public delegate void FileChangedDelegate(IVirtualFile changedFile);

    public delegate void FolderChangedDelegate(IVirtualFolder changedFolder, IVirtualFolder[]? previousPathFromRoot);
}