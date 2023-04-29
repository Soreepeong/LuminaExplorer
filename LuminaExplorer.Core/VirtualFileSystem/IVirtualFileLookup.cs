using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Data;
using Lumina.Data.Attributes;
using Lumina.Data.Structs;

namespace LuminaExplorer.Core.VirtualFileSystem;

public interface IVirtualFileLookup : IDisposable {
    public IVirtualFile File { get; }

    public FileType Type { get; }

    public long Size { get; }

    public long ReservedBytes { get; }

    public long OccupiedBytes { get; }

    public Stream CreateStream();

    public Task<byte[]> ReadAll(CancellationToken cancellationToken = default);

    public Task<FileResource> AsFileResource(CancellationToken cancellationToken = default);

    public Task<T> AsFileResource<T>(CancellationToken cancellationToken = default) where T : FileResource;

    protected static HashSet<Type> FindPossibleTypes(IVirtualFileLookup lookup, LuminaBinaryReader reader) {
        var magic = lookup.Size >= 4 ? reader.ReadUInt32() : 0;

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
        var possibleTypes = new HashSet<Type>();

        switch (lookup.Type) {
            case FileType.Empty:
                break;

            case FileType.Standard: {
                {
                    if (typeByExt.TryGetValue(
                            Path.GetExtension(lookup.File.Name).ToLowerInvariant(),
                            out var type))
                        possibleTypes.Add(type);
                }

                {
                    if (VirtualFileSystemExtensions.GetFileResourceTypeByMagic(magic, out var type))
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

        return possibleTypes;
    }
}
