using System.Reflection;
using System.Runtime.InteropServices;
using Lumina;
using Lumina.Data;
using Lumina.Data.Attributes;
using Lumina.Data.Structs;
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

    /// <summary>Used only for constructiong MdlFile.</summary>
    private ModelBlock _modelBlock;


    internal unsafe VirtualFileLookup(VirtualSqPackTree tree, VirtualFile virtualFile, LuminaBinaryReader reader) {
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
        _modelBlock = new();
        fixed (void* p = &_modelBlock) {
            var mdlBlockReadSize = reader.Read(new Span<byte>(p, Marshal.SizeOf<ModelBlock>()));
            if (mdlBlockReadSize < Marshal.SizeOf<SqPackFileInfo>()) {
                reader.Close();
                throw new InvalidDataException();
            }

            if (_modelBlock.Type == FileType.Model && mdlBlockReadSize < Marshal.SizeOf<ModelBlock>()) {
                reader.Close();
                throw new InvalidDataException();
            }
        }

        Type = _modelBlock.Type;
        Size = _modelBlock.RawFileSize;
        ReservedSpaceUnits = _modelBlock.NumberOfBlocks;
        OccupiedSpaceUnits = _modelBlock.UsedNumberOfBlocks;

        _dataStream = new(() => Type switch {
            FileType.Empty => new EmptyVirtualFileStream(ReservedSpaceUnits, OccupiedSpaceUnits),
            FileType.Standard => new StandardVirtualFileStream(reader, virtualFile.Offset, _modelBlock.Size,
                _modelBlock.Version, Size, ReservedSpaceUnits, OccupiedSpaceUnits),
            FileType.Model => new ModelVirtualFileStream(reader, virtualFile.Offset, _modelBlock),
            FileType.Texture => new TextureVirtualFileStream(reader, virtualFile.Offset, _modelBlock.Size,
                _modelBlock.Version, Size, ReservedSpaceUnits, OccupiedSpaceUnits, _tree.PlatformId),
            _ => throw new NotSupportedException()
        });
    }

    public BaseVirtualFileStream DataStream => _dataStream.Value;
    
    public Task<FileResource> AsFileResource(Type type) {
        const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        if (!type.IsAssignableTo(typeof(FileResource)))
            throw new ArgumentException(null, nameof(type));

        return Task.Run(() => {
            var buffer = new byte[_dataStream.Value.Length];
            _dataStream.Value.WithSeek(0).ReadFully(new(buffer));

            var file = (FileResource) Activator.CreateInstance(type)!;
            var luminaFileInfo = new LuminaFileInfo {
                HeaderSize = _modelBlock.Size,
                Type = _modelBlock.Type,
                BlockCount = Type == FileType.Model
                    ? _modelBlock.UsedNumberOfBlocks
                    : _modelBlock.Version,
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
                if (VirtualFile.NameResolved) {
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
