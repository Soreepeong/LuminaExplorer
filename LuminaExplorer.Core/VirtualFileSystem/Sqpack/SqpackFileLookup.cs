using System;
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
        _core = new(tree, sqpackFile, datPath);

    private SqpackFileLookup(VirtualFileLookupCore? core) {
        _core = core;
        if (_core is null)
            throw new ObjectDisposedException(nameof(ModelSqpackFileStream));

        _core.AddRef();
    }

    ~SqpackFileLookup() {
        ReleaseUnmanagedResources();
    }

    private VirtualFileLookupCore Core => _core ?? throw new ObjectDisposedException(nameof(SqpackFileLookup));

    private void ReleaseUnmanagedResources() {
        _core?.DecRef();
        _core = null;
    }

    public void Dispose() {
        ReleaseUnmanagedResources();
        GC.SuppressFinalize(this);
    }

    public object Clone() => new SqpackFileLookup(_core);

    public SqpackFile FileTyped => Core.FileTyped;

    public IVirtualFile File => Core.FileTyped;

    public FileType Type => Core.Type;

    public long Size => Core.Size;

    public long ReservedBytes => Core.ReservedBytes;

    public long OccupiedBytes => Core.OccupiedBytes;

    public Stream CreateStream() => Core.CreateStream();

    public Task<byte[]> ReadAll(CancellationToken cancellationToken = default) => Core.ReadAll(cancellationToken);

    public Task<FileResource> AsFileResource(CancellationToken cancellationToken = default) =>
        Core.AsFileResource(cancellationToken);
    
    public Task<T> AsFileResource<T>(CancellationToken cancellationToken = default) where T : FileResource =>
        Core.AsFileResource<T>(cancellationToken);

    private class VirtualFileLookupCore : IVirtualFileLookup{
        private int _refcount = 1;

        private readonly SqpackFileSystem _vfs;
        private readonly Lazy<BaseSqpackFileStream> _dataStream;

        public readonly SqpackFile FileTyped;

        private readonly SqPackFileInfo _fileInfo;
        private readonly ModelBlock? _modelBlock;

        internal VirtualFileLookupCore(SqpackFileSystem vfs, SqpackFile file, string datPath) {
            _vfs = vfs;
            FileTyped = file;

            using var reader = new LuminaBinaryReader(System.IO.File.OpenRead(datPath), vfs.PlatformId);
            reader.Position = file.Offset;

            _fileInfo = reader.WithSeek(file.Offset).ReadStructure<SqPackFileInfo>();
            _modelBlock = _fileInfo.Type == FileType.Model
                ? reader.WithSeek(file.Offset).ReadStructure<ModelBlock>()
                : null;

            Type = _fileInfo.Type;
            Size = _fileInfo.RawFileSize;
            unsafe {
                ReservedBytes = (long)_fileInfo.__unknown[0] << 7;
                OccupiedBytes = (long)_fileInfo.__unknown[1] << 7;
            }

            _dataStream = new(() => {
                BaseSqpackFileStream result = Type switch {
                    FileType.Empty => new EmptySqpackFileStream(_vfs.PlatformId),
                    FileType.Standard => new StandardSqpackFileStream(datPath, _vfs.PlatformId, file.Offset,
                        _fileInfo),
                    FileType.Model => new ModelSqpackFileStream(datPath, _vfs.PlatformId, file.Offset,
                        _modelBlock!.Value),
                    FileType.Texture => new TextureSqpackFileStream(datPath, _vfs.PlatformId, file.Offset, _fileInfo),
                    _ => throw new NotSupportedException(),
                };

                result.CloseButOpenAgainWhenNecessary();

                return result;
            });
        }

        public void Dispose() {
            throw new NotImplementedException();
        }

        public IVirtualFile File => FileTyped;
        
        public FileType Type { get; }
        
        public long Size { get; }
        
        public long ReservedBytes { get; }
        
        public long OccupiedBytes { get; }

        public void AddRef() => Interlocked.Increment(ref _refcount);

        public void DecRef() {
            if (Interlocked.Decrement(ref _refcount) != 0)
                return;

            if (_dataStream.IsValueCreated)
                _dataStream.Value.Dispose();
        }

        public Stream CreateStream() => new BufferedStream(_dataStream.Value.Clone(true));

        public async Task<byte[]> ReadAll(CancellationToken cancellationToken = default) {
            await using var clonedStream = CreateStream();
            var buffer = new byte[clonedStream.Length];
            await clonedStream.ReadExactlyAsync(new(buffer), cancellationToken);
            return buffer;
        }

        private FileResource AsFileResourceImpl(LuminaBinaryReader reader, byte[] buffer, Type type) {
            const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            if (!type.IsAssignableTo(typeof(FileResource)))
                throw new ArgumentException(null, nameof(type));

            var file = (FileResource) Activator.CreateInstance(type)!;
            var luminaFileInfo = new LuminaFileInfo {
                HeaderSize = _fileInfo.Size,
                Type = _fileInfo.Type,
                BlockCount = Type == FileType.Model
                    ? _modelBlock!.Value.UsedNumberOfBlocks
                    : _fileInfo.NumberOfBlocks,
            };
            typeof(LuminaFileInfo)
                .GetProperty("Offset", bindingFlags)
                !.SetValue(luminaFileInfo, FileTyped.Offset);
            if (Type == FileType.Model) {
                typeof(LuminaFileInfo)
                    .GetProperty("ModelBlock", bindingFlags)
                    !.SetValue(luminaFileInfo, _modelBlock);
            }

            var pfp = GameData.ParseFilePath(_vfs.GetFullPath(FileTyped));

            typeof(FileResource).GetProperty("FileInfo", bindingFlags)!.SetValue(file, luminaFileInfo);
            typeof(FileResource).GetProperty("FilePath", bindingFlags)!.SetValue(file, pfp);
            typeof(FileResource).GetProperty("Data", bindingFlags)!.SetValue(file, buffer);
            typeof(FileResource).GetProperty("Reader", bindingFlags)!.SetValue(file, reader);
            typeof(FileResource).GetMethod("LoadFile", bindingFlags)!.Invoke(file, null);
            return file;
        }

        public Task<T> AsFileResource<T>(CancellationToken cancellationToken = default) where T : FileResource =>
            Task.Factory.StartNew(
                () => ReadAll(cancellationToken)
                    .ContinueWith(buffer => {
                        var reader = new LuminaBinaryReader(buffer.Result, _vfs.PlatformId);
                        try {
                            cancellationToken.ThrowIfCancellationRequested();
                            return (T) AsFileResourceImpl(reader.WithSeek(0), buffer.Result, typeof(T));
                        } catch(Exception) {
                            reader.Dispose();
                            throw;
                        }
                    }, cancellationToken),
                cancellationToken,
                TaskCreationOptions.None,
                TaskScheduler.Default
            ).Unwrap();

        public Task<FileResource> AsFileResource(CancellationToken cancellationToken = default) =>
            Task.Factory.StartNew(
                () => ReadAll(cancellationToken)
                    .ContinueWith(buffer => {
                        var reader = new LuminaBinaryReader(buffer.Result, _vfs.PlatformId);
                        var possibleTypes = IVirtualFileLookup.FindPossibleTypes(this, reader);

                        foreach (var f in possibleTypes) {
                            cancellationToken.ThrowIfCancellationRequested();
                            try {
                                return AsFileResourceImpl(reader.WithSeek(0), buffer.Result, f);
                            } catch (Exception) {
                                // pass 
                            }
                        }

                        cancellationToken.ThrowIfCancellationRequested();
                        return AsFileResourceImpl(reader.WithSeek(0), buffer.Result, typeof(FileResource));
                    }, cancellationToken),
                cancellationToken,
                TaskCreationOptions.None,
                TaskScheduler.Default
            ).Unwrap();
    }
}
