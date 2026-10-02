using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Lumina;
using Lumina.Data;
using Lumina.Data.Structs;
using LuminaExplorer.Core.Util;
using LuminaExplorer.Core.VirtualFileSystem.Sqpack.SqpackFileStream;

namespace LuminaExplorer.Core.VirtualFileSystem.Sqpack;

public sealed class SqpackFileLookup : ICloneable, IVirtualFileLookup {
    private VirtualFileLookupCore? _core;

    public SqpackFileLookup(SqpackFileSystem tree, SqpackFile sqpackFile, string datPath) =>
        this._core = new(tree, sqpackFile, datPath);

    private SqpackFileLookup(VirtualFileLookupCore? core)
    {
        this._core = core;
        if (this._core is null)
            throw new ObjectDisposedException(nameof(SqpackFileLookup));

        this._core.AddRef();
    }

    ~SqpackFileLookup()
    {
        this.ReleaseUnmanagedResources();
    }

    private VirtualFileLookupCore Core => this._core ?? throw new ObjectDisposedException(nameof(SqpackFileLookup));

    private void ReleaseUnmanagedResources()
    {
        this._core?.DecRef();
        this._core = null;
    }

    public void Dispose()
    {
        this.ReleaseUnmanagedResources();
        GC.SuppressFinalize(this);
    }

    public object Clone() => new SqpackFileLookup(this._core);

    public SqpackFile FileTyped => this.Core.FileTyped;

    public IVirtualFile File => this.Core.FileTyped;

    public FileType Type => this.Core.Type;

    public long Size => this.Core.Size;

    public long ReservedBytes => this.Core.ReservedBytes;

    public long OccupiedBytes => this.Core.OccupiedBytes;

    public Stream CreateStream() => this.Core.CreateStream();

    public Task<byte[]> ReadAll(CancellationToken cancellationToken = default) => this.Core.ReadAll(cancellationToken);

    public Task<FileResource> AsFileResource(CancellationToken cancellationToken = default) =>
        this.Core.AsFileResource(cancellationToken);

    public Task<T> AsFileResource<T>(CancellationToken cancellationToken = default) where T : FileResource =>
        this.Core.AsFileResource<T>(cancellationToken);

    private class VirtualFileLookupCore : IVirtualFileLookup {
        private int _refcount = 1;

        private readonly SqpackFileSystem _vfs;
        private readonly Lazy<BaseSqpackFileStream> _dataStream;

        public readonly SqpackFile FileTyped;

        private readonly SqPackFileInfo _fileInfo;
        private readonly ModelBlock? _modelBlock;

        internal VirtualFileLookupCore(SqpackFileSystem vfs, SqpackFile file, string datPath)
        {
            this._vfs = vfs;
            this.FileTyped = file;

            using var reader = new LuminaBinaryReader(System.IO.File.OpenRead(datPath), vfs.PlatformId);
            reader.Position = file.Offset;

            this._fileInfo = reader.WithSeek(file.Offset).ReadStructure<SqPackFileInfo>();
            this._modelBlock = this._fileInfo.Type == FileType.Model
                ? reader.WithSeek(file.Offset).ReadStructure<ModelBlock>()
                : null;

            this.Type = this._fileInfo.Type;
            this.Size = this._fileInfo.RawFileSize;
            unsafe {
                this.ReservedBytes = (long) this._fileInfo.__unknown[0] << 7;
                this.OccupiedBytes = (long) this._fileInfo.__unknown[1] << 7;
            }

            this._dataStream = new(
                () => {
                    BaseSqpackFileStream result = this.Type switch {
                        FileType.Empty => new EmptySqpackFileStream(this._vfs.PlatformId),
                        FileType.Standard => new StandardSqpackFileStream(
                            datPath,
                            this._vfs.PlatformId,
                            file.Offset,
                            this._fileInfo),
                        FileType.Model => new ModelSqpackFileStream(
                            datPath,
                            this._vfs.PlatformId,
                            file.Offset,
                            this._modelBlock!.Value),
                        FileType.Texture => new TextureSqpackFileStream(
                            datPath,
                            this._vfs.PlatformId,
                            file.Offset,
                            this._fileInfo),
                        _ => throw new NotSupportedException(),
                    };

                    result.CloseButOpenAgainWhenNecessary();

                    return result;
                });
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public IVirtualFile File => this.FileTyped;

        public FileType Type { get; }

        public long Size { get; }

        public long ReservedBytes { get; }

        public long OccupiedBytes { get; }

        public void AddRef() => Interlocked.Increment(ref this._refcount);

        public void DecRef()
        {
            if (Interlocked.Decrement(ref this._refcount) != 0)
                return;

            if (this._dataStream.IsValueCreated) this._dataStream.Value.Dispose();
        }

        public Stream CreateStream() => new BufferedStream(this._dataStream.Value.Clone(true));

        public async Task<byte[]> ReadAll(CancellationToken cancellationToken = default)
        {
            await using var clonedStream = this.CreateStream();
            var buffer = new byte[clonedStream.Length];
            await clonedStream.ReadExactlyAsync(new(buffer), cancellationToken);
            return buffer;
        }

        private FileResource AsFileResourceImpl(LuminaBinaryReader reader, byte[] buffer, Type type)
        {
            const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            if (!type.IsAssignableTo(typeof(FileResource)))
                throw new ArgumentException(null, nameof(type));

            type = IVirtualFileLookup.ResolveFileResourceType(type);
            var file = (FileResource) Activator.CreateInstance(type)!;
            var luminaFileInfo = new LuminaFileInfo {
                HeaderSize = this._fileInfo.Size,
                Type = this._fileInfo.Type,
                BlockCount = this.Type == FileType.Model
                    ? this._modelBlock!.Value.UsedNumberOfBlocks
                    : this._fileInfo.NumberOfBlocks,
            };
            typeof(LuminaFileInfo)
                .GetProperty("Offset", bindingFlags)
                !.SetValue(luminaFileInfo, this.FileTyped.Offset);
            if (this.Type == FileType.Model) {
                typeof(LuminaFileInfo)
                    .GetProperty("ModelBlock", bindingFlags)
                    !.SetValue(luminaFileInfo, this._modelBlock);
            }

            var fullPath = this._vfs.GetFullPath(this.FileTyped);
            var pfp = GameData.ParseFilePath(fullPath);
            if (pfp is null) {
                // Lumina 7+ returns null for paths that are 260+ characters long or have no folder.
                pfp = new();
                typeof(ParsedFilePath).GetProperty("Path", bindingFlags)!.SetValue(pfp, fullPath.ToLowerInvariant());
            }

            typeof(FileResource).GetProperty("FileInfo", bindingFlags)!.SetValue(file, luminaFileInfo);
            typeof(FileResource).GetProperty("FilePath", bindingFlags)!.SetValue(file, pfp);
            typeof(FileResource).GetProperty("Data", bindingFlags)!.SetValue(file, buffer);
            typeof(FileResource).GetProperty("Reader", bindingFlags)!.SetValue(file, reader);
            typeof(FileResource).GetMethod("LoadFile", bindingFlags)!.Invoke(file, null);
            return file;
        }

        public Task<T> AsFileResource<T>(CancellationToken cancellationToken = default) where T : FileResource =>
            Task.Factory.StartNew(
                () => this.ReadAll(cancellationToken)
                    .ContinueWith(
                        buffer => {
                            var reader = new LuminaBinaryReader(buffer.Result, this._vfs.PlatformId);
                            try {
                                cancellationToken.ThrowIfCancellationRequested();
                                return (T) this.AsFileResourceImpl(reader.WithSeek(0), buffer.Result, typeof(T));
                            } catch (Exception) {
                                reader.Dispose();
                                throw;
                            }
                        },
                        cancellationToken),
                cancellationToken,
                TaskCreationOptions.None,
                TaskScheduler.Default
            ).Unwrap();

        public Task<FileResource> AsFileResource(CancellationToken cancellationToken = default) =>
            Task.Factory.StartNew(
                () => this.ReadAll(cancellationToken)
                    .ContinueWith(
                        buffer => {
                            var reader = new LuminaBinaryReader(buffer.Result, this._vfs.PlatformId);
                            var possibleTypes = IVirtualFileLookup.FindPossibleTypes(this, reader);

                            foreach (var f in possibleTypes) {
                                cancellationToken.ThrowIfCancellationRequested();
                                try {
                                    return this.AsFileResourceImpl(reader.WithSeek(0), buffer.Result, f);
                                } catch (Exception e) {
                                    Debug.WriteLine(e);
                                    // pass 
                                }
                            }

                            cancellationToken.ThrowIfCancellationRequested();
                            return this.AsFileResourceImpl(reader.WithSeek(0), buffer.Result, typeof(FileResource));
                        },
                        cancellationToken),
                cancellationToken,
                TaskCreationOptions.None,
                TaskScheduler.Default
            ).Unwrap();
    }
}
