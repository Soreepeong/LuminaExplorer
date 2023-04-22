using System.Reflection;
using Lumina;
using Lumina.Data;
using Lumina.Data.Attributes;
using Lumina.Data.Structs;
using LuminaExplorer.Core.LazySqPackTree.VirtualFileStream;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.LazySqPackTree;

public sealed class VirtualFileLookup : ICloneable, IDisposable {
    private VirtualFileLookupCore? _core;

    public VirtualFileLookup(VirtualSqPackTree tree, VirtualFile virtualFile, string datPath) =>
        _core = new(tree, virtualFile, datPath);

    private VirtualFileLookup(VirtualFileLookupCore? core) {
        _core = core;
        if (_core is null)
            throw new ObjectDisposedException(nameof(ModelVirtualFileStream));

        _core.AddRef();
    }

    ~VirtualFileLookup() {
        ReleaseUnmanagedResources();
    }

    private VirtualFileLookupCore Core => _core ?? throw new ObjectDisposedException(nameof(VirtualFileLookup));

    private void ReleaseUnmanagedResources() {
        _core?.DecRef();
        _core = null;
    }

    public void Dispose() {
        ReleaseUnmanagedResources();
        GC.SuppressFinalize(this);
    }

    public object Clone() => new VirtualFileLookup(_core);

    public VirtualFile File => Core.File;

    public FileType Type => Core.Type;

    public uint Size => Core.Size;

    public uint ReservedSpaceUnits => Core.ReservedSpaceUnits;

    public uint OccupiedSpaceUnits => Core.OccupiedSpaceUnits;

    public ulong ReservedBytes => Core.ReservedBytes;

    public ulong OccupiedBytes => Core.OccupiedBytes;

    public Stream CreateStream() => Core.CreateStream();

    public Task<byte[]> ReadAll(CancellationToken cancellationToken = default) => Core.ReadAll(cancellationToken);

    public Task<FileResource> AsFileResource(CancellationToken cancellationToken = default) =>
        Core.AsFileResource(cancellationToken);
    
    public Task<T> AsFileResource<T>(CancellationToken cancellationToken = default) where T : FileResource =>
        Core.AsFileResource<T>(cancellationToken);

    private class VirtualFileLookupCore {
        private int _refcount = 1;

        private readonly VirtualSqPackTree _tree;
        private readonly Lazy<BaseVirtualFileStream> _dataStream;

        public readonly VirtualFile File;
        public readonly FileType Type;
        public readonly uint Size;
        public readonly uint ReservedSpaceUnits;
        public readonly uint OccupiedSpaceUnits;

        private readonly SqPackFileInfo _fileInfo;
        private readonly ModelBlock? _modelBlock;

        internal VirtualFileLookupCore(VirtualSqPackTree tree, VirtualFile file, string datPath) {
            _tree = tree;
            File = file;

            using var reader = new LuminaBinaryReader(System.IO.File.OpenRead(datPath), tree.PlatformId);
            reader.Position = file.Offset;

            _fileInfo = reader.WithSeek(file.Offset).ReadStructure<SqPackFileInfo>();
            _modelBlock = _fileInfo.Type == FileType.Model
                ? reader.WithSeek(file.Offset).ReadStructure<ModelBlock>()
                : null;

            Type = _fileInfo.Type;
            Size = _fileInfo.RawFileSize;
            unsafe {
                ReservedSpaceUnits = _fileInfo.__unknown[0];
                OccupiedSpaceUnits = _fileInfo.__unknown[1];
            }

            _dataStream = new(() => {
                BaseVirtualFileStream result = Type switch {
                    FileType.Empty => new EmptyVirtualFileStream(_tree.PlatformId),
                    FileType.Standard => new StandardVirtualFileStream(datPath, _tree.PlatformId, file.Offset,
                        _fileInfo),
                    FileType.Model => new ModelVirtualFileStream(datPath, _tree.PlatformId, file.Offset,
                        _modelBlock!.Value),
                    FileType.Texture => new TextureVirtualFileStream(datPath, _tree.PlatformId, file.Offset, _fileInfo),
                    _ => throw new NotSupportedException(),
                };

                result.CloseButOpenAgainWhenNecessary();

                return result;
            });
        }

        public ulong ReservedBytes => (ulong) ReservedSpaceUnits << 7;
        public ulong OccupiedBytes => (ulong) OccupiedSpaceUnits << 7;

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
                !.SetValue(luminaFileInfo, File.Offset);
            if (Type == FileType.Model) {
                typeof(LuminaFileInfo)
                    .GetProperty("ModelBlock", bindingFlags)
                    !.SetValue(luminaFileInfo, _modelBlock);
            }

            typeof(FileResource)
                .GetProperty("FilePath", bindingFlags)
                !.SetValue(file, GameData.ParseFilePath(_tree.GetFullPath(File)));
            typeof(FileResource)
                .GetProperty("Data", bindingFlags)
                !.SetValue(file, buffer);
            typeof(FileResource)
                .GetProperty("Reader", bindingFlags)
                !.SetValue(file, reader);
            typeof(FileResource)
                .GetMethod("LoadFile", bindingFlags)
                !.Invoke(file, null);
            return file;
        }

        public Task<T> AsFileResource<T>(CancellationToken cancellationToken = default) where T : FileResource =>
            Task.Factory.StartNew(
                () => ReadAll(cancellationToken)
                    .ContinueWith(buffer => {
                        var reader = new LuminaBinaryReader(buffer.Result, _tree.PlatformId);
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
                        var reader = new LuminaBinaryReader(buffer.Result, _tree.PlatformId);

                        var magic = Size >= 4 ? reader.ReadUInt32() : 0;

                        var fileResourceType = typeof(FileResource);
                        var allResourceTypes = AppDomain.CurrentDomain.GetAssemblies()
                            .SelectMany(x => x.GetTypes())
                            .Where(x => fileResourceType.IsAssignableFrom(x) && x != fileResourceType)
                            .ToArray();

                        var typeByExt = allResourceTypes.ToDictionary(
                            x => (x.GetCustomAttribute<FileExtensionAttribute>()?.Extension ?? $".{x.Name[..^4]}")
                                .ToLowerInvariant(),
                            x => x);

                        typeByExt[".atex"] = typeByExt[".tex"];

                        var typeByMagic = new Dictionary<uint, Type> {
                            {
                                0x42444553u, typeByExt[".scd"]
                            },
                        };
                        var possibleTypes = new HashSet<Type>();

                        switch (Type) {
                            case FileType.Empty:
                                break;

                            case FileType.Standard: {
                                if (File.NameResolveAttempted) {
                                    if (typeByExt.TryGetValue(
                                            Path.GetExtension(File.Name).ToLowerInvariant(),
                                            out var type))
                                        possibleTypes.Add(type);
                                }

                                {
                                    if (typeByMagic.TryGetValue(magic, out var type))
                                        possibleTypes.Add(type);
                                }

                                break;
                            }

                            case FileType.Model:
                                possibleTypes.Add(typeByExt[".mdl"]);
                                break;

                            case FileType.Texture:
                                possibleTypes.Add(typeByExt[".tex"]);
                                break;

                            default:
                                throw new NotSupportedException();
                        }

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
