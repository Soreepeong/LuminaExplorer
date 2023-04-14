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
    public readonly VirtualFile VirtualFile;
    public readonly PlatformId PlatformId;
    public readonly BaseVirtualFileStream DataStream;
    public readonly FileType Type;
    public readonly uint Size;
    public readonly uint ReservedSpaceUnits;
    public readonly uint OccupiedSpaceUnits;

    private ModelBlock _modelBlock;

    internal unsafe VirtualFileLookup(VirtualFile virtualFile, PlatformId platformId, Stream datStream) {
        VirtualFile = virtualFile;
        PlatformId = platformId;
        datStream.Position = virtualFile.Offset;

        _modelBlock = new();
        fixed (void* p = &_modelBlock) {
            var mdlBlockReadSize = datStream.Read(new(p, Marshal.SizeOf<ModelBlock>()));
            if (mdlBlockReadSize < Marshal.SizeOf<SqPackFileInfo>()) {
                datStream.Close();
                throw new InvalidDataException();
            }
        }

        Type = _modelBlock.Type;
        Size = _modelBlock.RawFileSize;
        ReservedSpaceUnits = _modelBlock.NumberOfBlocks;
        OccupiedSpaceUnits = _modelBlock.UsedNumberOfBlocks;

        DataStream = Type switch {
            FileType.Empty => new EmptyVirtualFileStream(ReservedSpaceUnits, OccupiedSpaceUnits),
            FileType.Standard => new StandardVirtualFileStream(datStream, virtualFile.Offset, _modelBlock.Size,
                _modelBlock.Version, Size, ReservedSpaceUnits, OccupiedSpaceUnits),
            FileType.Model => new ModelVirtualFileStream(datStream, virtualFile.Offset, Size, ReservedSpaceUnits,
                OccupiedSpaceUnits),
            FileType.Texture => new EmptyVirtualFileStream(ReservedSpaceUnits, OccupiedSpaceUnits), // TODO
            _ => throw new NotSupportedException()
        };
    }

    public Task<FileResource> AsFileResource(Type type) {
        const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        if (!type.IsAssignableTo(typeof(FileResource)))
            throw new ArgumentException(null, nameof(type));

        return Task.Run(() => {
            var buffer = new byte[DataStream.Length];
            DataStream.SeekIfNecessary(0).ReadFully(new(buffer));

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
                !.SetValue(file, new LuminaBinaryReader(buffer, PlatformId));
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
                            new BinaryReader(DataStream.SeekIfNecessary(0)).ReadUInt32(), out var type))
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
        DataStream.Dispose();
    }
}
