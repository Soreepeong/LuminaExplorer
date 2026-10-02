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

    private readonly LruCache<SqpackFile, SqpackFileLookup> _fileLookups = new(4096, true);

    public readonly SqpackFolder RootFolderTyped = SqpackFolder.CreateRoot();
    public readonly DirectoryInfo InstallationSqPackDirectory;
    public readonly PlatformId PlatformId;

    /// <summary>Known paths, used to resolve file and folder names.</summary>
    public readonly HashDatabase HashDatabase;

    public readonly GameData GameData;

    public event IVirtualFileSystem.FolderChangedDelegate? FolderChanged;
    public event IVirtualFileSystem.FileChangedDelegate? FileChanged;

    public SqpackFileSystem(HashDatabase hashDatabase, GameData gameData)
    {
        this.InstallationSqPackDirectory = gameData.DataPath;
        this.PlatformId = gameData.Options.CurrentPlatform;
        this.HashDatabase = hashDatabase;
        this.GameData = gameData;

        this._childFoldersResolvers.Add(
            this.RootFolderTyped,
            new(
                () => Task.Run(
                    () => {
                        this._treeStructureLock.EnterReadLock();
                        try {
                            foreach (var (categoryId, categoryName) in Repository.CategoryIdToNameMap) {
                                var repos = gameData.Repositories
                                    .Where(x => x.Value.Categories.GetValueOrDefault(categoryId)?.Count is > 0)
                                    .ToDictionary(x => x.Key, x => x.Value.Categories[categoryId]);
                                switch (repos.Count) {
                                    case 1:
                                        this.PopulateFolderResolverFor(
                                            this.UnsafeGetOrCreateSubfolder(this.RootFolderTyped, categoryName),
                                            hashDatabase,
                                            categoryName,
                                            repos.First().Value);
                                        break;

                                    case > 1: {
                                        var categoryNode = this.UnsafeGetOrCreateSubfolder(
                                            this.RootFolderTyped,
                                            categoryName);
                                        foreach (var (repoName, chunks) in repos) {
                                            this.PopulateFolderResolverFor(
                                                this.UnsafeGetOrCreateSubfolder(categoryNode, repoName),
                                                hashDatabase,
                                                $"{categoryName}/{repoName}",
                                                chunks);
                                        }

                                        break;
                                    }
                                }
                            }
                        } finally {
                            this._treeStructureLock.ExitReadLock();
                        }

                        return this.RootFolder;
                    })));
    }

    public IVirtualFolder RootFolder => this.RootFolderTyped;

    public void Dispose()
    {
        lock (this._fileLookups) this._fileLookups.Dispose();
        this.FolderChanged = null;
        this.FileChanged = null;
    }

    public SqpackFileLookup GetLookup(SqpackFile file)
    {
        SqpackFileLookup? data;
        lock (this._fileLookups) {
            if (this._fileLookups.TryGet(file, out data))
                return (SqpackFileLookup) data.Clone();
        }

        var cat = unchecked((byte) (file.IndexId >> 16));
        var ex = unchecked((byte) (file.IndexId >> 8));
        var chunk = unchecked((byte) file.IndexId);
        var repoName = (file.IndexId & 0x00FF00) == 0
            ? "ffxiv"
            : $"ex{(file.IndexId >> 8) & 0xFF:D}";
        var fileName = Repository.BuildDatStr(cat, ex, chunk, this.PlatformId, $"dat{file.DataFileId}");
        var datPath = Path.Combine(this.InstallationSqPackDirectory.FullName, repoName, fileName);

        data = new(this, file, datPath);

        lock (this._fileLookups) this._fileLookups.Add(file, data);
        return (SqpackFileLookup) data.Clone();
    }

    public IVirtualFileLookup GetLookup(IVirtualFile file) => this.GetLookup((SqpackFile) file);

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

        public byte DataFileId => (byte) ((this.Data & 0b1110) >> 1);

        public long Offset => (this.Data & ~0xF) * 0x08;
    }
}
