using System.Reflection;
using Lumina;
using Lumina.Data;
using Lumina.Data.Attributes;
using Lumina.Data.Structs;
using LuminaExplorer.Core.LazySqPackTree.VirtualFileStream;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.LazySqPackTree;

public sealed class VirtualFileLookup : IDisposable {
    private readonly VirtualSqPackTree _tree;
    private readonly Lazy<BaseVirtualFileStream> _dataStream;

    public readonly VirtualFile VirtualFile;
    public readonly FileType Type;
    public readonly uint Size;
    public readonly uint ReservedSpaceUnits;
    public readonly uint OccupiedSpaceUnits;

    private readonly SqPackFileInfo _fileInfo;
    private readonly ModelBlock? _modelBlock;

    internal VirtualFileLookup(VirtualSqPackTree tree, VirtualFile virtualFile, LuminaBinaryReader reader) {
        _tree = tree;
        VirtualFile = virtualFile;
        reader.Position = virtualFile.Offset;

        _fileInfo = reader.WithSeek(virtualFile.Offset).ReadStructure<SqPackFileInfo>();
        _modelBlock = _fileInfo.Type == FileType.Model
            ? reader.WithSeek(virtualFile.Offset).ReadStructure<ModelBlock>()
            : null;

        Type = _fileInfo.Type;
        Size = _fileInfo.RawFileSize;
        unsafe {
            ReservedSpaceUnits = _fileInfo.__unknown[0];
            OccupiedSpaceUnits = _fileInfo.__unknown[1];
        }

        _dataStream = new(() => Type switch {
            FileType.Empty => new EmptyVirtualFileStream(_tree.PlatformId),
            FileType.Standard => new StandardVirtualFileStream(reader, virtualFile.Offset, _fileInfo),
            FileType.Model => new ModelVirtualFileStream(reader, virtualFile.Offset, _modelBlock!.Value),
            FileType.Texture => new TextureVirtualFileStream(reader, virtualFile.Offset, _fileInfo),
            _ => throw new NotSupportedException()
        });
    }

    public long ReservedBlockBytes => (long) ReservedSpaceUnits << 7;
    public long OccupiedBlockBytes => (long) OccupiedSpaceUnits << 7;

    public BaseVirtualFileStream DataStream => _dataStream.Value;

    public Stream CreateStream() => new BufferedStream((Stream) DataStream.Clone());

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
            !.SetValue(luminaFileInfo, VirtualFile.Offset);
        if (Type == FileType.Model) {
            typeof(LuminaFileInfo)
                .GetProperty("ModelBlock", bindingFlags)
                !.SetValue(luminaFileInfo, _modelBlock);
        }

        typeof(FileResource)
            .GetProperty("FilePath", bindingFlags)
            !.SetValue(file, GameData.ParseFilePath(VirtualFile.FullPath));
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

    public Task<FileResource> AsFileResource(CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadAll(cancellationToken), cancellationToken)
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
                var possibleTypes = new List<Type>();

                switch (Type) {
                    case FileType.Empty:
                        break;

                    case FileType.Standard: {
                        if (VirtualFile.NameResolveAttempted) {
                            if (typeByExt.TryGetValue(Path.GetExtension(VirtualFile.Name).ToLowerInvariant(),
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

                foreach (var f in Enumerable.Reverse(possibleTypes)) {
                    cancellationToken.ThrowIfCancellationRequested();
                    try {
                        return AsFileResourceImpl(reader.WithSeek(0), buffer.Result, f);
                    } catch (Exception) {
                        // pass 
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                return AsFileResourceImpl(reader.WithSeek(0), buffer.Result, typeof(FileResource));
            }, cancellationToken);

    public void Dispose() {
        if (_dataStream.IsValueCreated)
            _dataStream.Value.Dispose();
    }
}
