using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Lumina;
using Lumina.Data;
using Lumina.Data.Structs;
using LuminaExplorer.Core.SqPackPath;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.VirtualFileSystem.Sqpack;

public sealed partial class SqpackFileSystem : IVirtualFileSystem {
    // Enter writer lock when nodes may be moved around across parents not in same hierarchy.
    private readonly ReaderWriterLockSlim _treeStructureLock = new();

    private readonly LruCache<VirtualFile, SqpackFileLookup> _fileLookups = new(4096, true);

    public readonly VirtualFolder RootFolderTyped = VirtualFolder.CreateRoot();
    public readonly DirectoryInfo InstallationSqPackDirectory;
    public readonly PlatformId PlatformId;

    public event IVirtualFileSystem.FolderChangedDelegate? FolderChanged;
    public event IVirtualFileSystem.FileChangedDelegate? FileChanged;

    public SqpackFileSystem(HashDatabase hashDatabase, GameData gameData) {
        InstallationSqPackDirectory = gameData.DataPath;
        PlatformId = gameData.Options.CurrentPlatform;

        _childFoldersResolvers.Add(RootFolderTyped, new(() => Task.Run(() => {
            _treeStructureLock.EnterReadLock();
            try {
                foreach (var (categoryId, categoryName) in Repository.CategoryIdToNameMap) {
                    var repos = gameData.Repositories
                        .Where(x => x.Value.Categories.GetValueOrDefault(categoryId)?.Count is > 0)
                        .ToDictionary(x => x.Key, x => x.Value.Categories[categoryId]);
                    switch (repos.Count) {
                        case 1:
                            PopulateFolderResolverFor(
                                UnsafeGetOrCreateSubfolder(RootFolderTyped, categoryName),
                                hashDatabase,
                                categoryName,
                                repos.First().Value);
                            break;

                        case > 1: {
                            var categoryNode = UnsafeGetOrCreateSubfolder(RootFolderTyped, categoryName);
                            foreach (var (repoName, chunks) in repos) {
                                PopulateFolderResolverFor(
                                    UnsafeGetOrCreateSubfolder(categoryNode, repoName),
                                    hashDatabase,
                                    $"{categoryName}/{repoName}",
                                    chunks);
                            }

                            break;
                        }
                    }
                }
            } finally {
                _treeStructureLock.ExitReadLock();
            }

            return RootFolder;
        })));
    }
    
    public IVirtualFolder RootFolder => RootFolderTyped;

    public void Dispose() {
        lock (_fileLookups)
            _fileLookups.Dispose();
        FolderChanged = null;
        FileChanged = null;
    }

    public SqpackFileLookup GetLookup(VirtualFile file) {
        SqpackFileLookup? data;
        lock (_fileLookups) {
            if (_fileLookups.TryGet(file, out data))
                return (SqpackFileLookup) data.Clone();
        }

        var cat = unchecked((byte) (file.IndexId >> 16));
        var ex = unchecked((byte) (file.IndexId >> 8));
        var chunk = unchecked((byte) file.IndexId);
        var repoName = (file.IndexId & 0x00FF00) == 0
            ? "ffxiv"
            : $"ex{(file.IndexId >> 8) & 0xFF:D}";
        var fileName = Repository.BuildDatStr(cat, ex, chunk, PlatformId, $"dat{file.DataFileId}");
        var datPath = Path.Combine(InstallationSqPackDirectory.FullName, repoName, fileName);

        data = new(this, file, datPath);

        lock (_fileLookups)
            _fileLookups.Add(file, data);
        return (SqpackFileLookup) data.Clone();
    }

    public IVirtualFileLookup GetLookup(IVirtualFile file) => GetLookup((VirtualFile) file);

    public string NormalizePath(params string[] pathComponents) =>
        Path.Join(pathComponents).Replace('\\', '/').Trim('/');

    [SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Local")]
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Local")]
    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct SqPackIndexFullPathEntry {
        public uint NameHash;
        public uint PathHash;
        public uint Data;
        public uint ConflictIndex;
        public fixed byte Name[0xF0];

        public byte DataFileId => (byte) ((Data & 0b1110) >> 1);

        public long Offset => (Data & ~0xF) * 0x08;
    }
}
