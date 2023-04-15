using System.Reflection;
using Lumina;
using Lumina.Data;
using Lumina.Data.Attributes;
using Lumina.Data.Structs;
using Lumina.Extensions;
using LuminaExplorer.LazySqPackTree.VirtualFileStream;
using LuminaExplorer.Util;

namespace LuminaExplorer.LazySqPackTree;

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

        // TODO: figure out why does it AVEs without these 
        var fileResourceType = typeof(FileResource);
        _ = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(x => x.GetTypes())
            .Where(x => fileResourceType.IsAssignableFrom(x) && x != fileResourceType)
            .ToArray();
        
        // Note: do not use ReadStructure/ReadFully.
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
            FileType.Empty => new EmptyVirtualFileStream(_tree.PlatformId, ReservedSpaceUnits, OccupiedSpaceUnits),
            FileType.Standard => new StandardVirtualFileStream(_tree.PlatformId, reader, virtualFile.Offset, _fileInfo.Size,
                _fileInfo.NumberOfBlocks, Size, ReservedSpaceUnits, OccupiedSpaceUnits),
            FileType.Model => new ModelVirtualFileStream(_tree.PlatformId, reader, virtualFile.Offset, _modelBlock!.Value),
            FileType.Texture => new TextureVirtualFileStream(_tree.PlatformId, reader, virtualFile.Offset, _fileInfo.Size, _fileInfo.NumberOfBlocks, Size, ReservedSpaceUnits, OccupiedSpaceUnits),
            _ => throw new NotSupportedException()
        });
    }

    public BaseVirtualFileStream DataStream => _dataStream.Value;
    
    public Task<FileResource> AsFileResource(Type type) {
        const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        if (!type.IsAssignableTo(typeof(FileResource)))
            throw new ArgumentException(null, nameof(type));

        return Task.Run(() => {
            byte[] buffer;

            using (var clonedStream = (Stream) _dataStream.Value.Clone()) {
                buffer = new byte[clonedStream.Length];
                clonedStream.WithSeek(0).ReadFully(new(buffer));
            }

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
                !.SetValue(file, new LuminaBinaryReader(buffer, _tree.PlatformId));
            typeof(FileResource)
                .GetMethod("LoadFile", bindingFlags)
                !.Invoke(file, null);
            return file;
        });
    }

    public async Task<FileResource> AsFileResource() {
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
                // TODO: deal with hidden files
                throw new FileNotFoundException();

            case FileType.Standard:
                if (VirtualFile.NameResolveAttempted) {
                    if (typeByExt.TryGetValue(Path.GetExtension(VirtualFile.Name).ToLowerInvariant(),
                            out var type))
                        possibleTypes.Add(type);
                }

                if (Size >= 4) {
                    if (typeByMagic.TryGetValue(
                            new BinaryReader(_dataStream.Value.WithSeek(0)).ReadUInt32(), out var type))
                        possibleTypes.Add(type);
                }

                break;

            case FileType.Model:
                possibleTypes.Add(typeByExt[".mdl"]);
                break;

            case FileType.Texture:
                possibleTypes.Add(typeByExt[".tex"]);
                break;

            default:
                throw new NotSupportedException();
        }

        possibleTypes.Reverse();
        foreach (var f in possibleTypes) {
            try {
                return await AsFileResource(f);
            } catch (Exception) {
                // pass 
            }
        }

        return await AsFileResource(typeof(FileResource));
    }

    public void Dispose() {
        if (_dataStream.IsValueCreated)
            _dataStream.Value.Dispose();
    }
}
