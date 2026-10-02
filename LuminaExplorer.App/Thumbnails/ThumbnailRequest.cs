using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using Lumina.Data;
using Lumina.Data.Attributes;
using Lumina.Data.Files;
using Lumina.Data.Files.Excel;
using Lumina.Data.Structs;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
using LuminaExplorer.Core.VirtualFileSystem;

namespace LuminaExplorer.App.Thumbnails;

/// <summary>Describes a file to create a thumbnail of, and the desired thumbnail.</summary>
public sealed class ThumbnailRequest {
    private static readonly Dictionary<string, Type> TypeByExtension = CreateTypeByExtensionMap();

    private readonly Lazy<string?> _fullPath;

    public ThumbnailRequest(
        IVirtualFileSystem? vfs,
        IVirtualFile file,
        IVirtualFileLookup lookup,
        uint? magic,
        PlatformId platformId,
        ThumbnailSettings settings)
    {
        this.File = file;
        this.Lookup = lookup;
        this.Name = file.Name;
        this.NameResolved = file.NameResolved;
        this.Extension = this.NameResolved ? Path.GetExtension(this.Name).ToLowerInvariant() : "";
        this.PackType = lookup.Type;
        this.Size = lookup.Size;
        this.Magic = magic;
        this.PlatformId = platformId;
        this.Settings = settings;
        this._fullPath = new(
            () => {
                try {
                    return vfs?.GetFullPath(file);
                } catch (Exception) {
                    return null;
                }
            });

        var types = new HashSet<Type>();
        switch (this.PackType) {
            case FileType.Model:
                types.Add(typeof(MdlFile));
                break;
            case FileType.Texture:
                types.Add(typeof(TexFile));
                break;
            case FileType.Standard:
                if (TypeByExtension.TryGetValue(this.Extension, out var typeByExtension))
                    types.Add(typeByExtension);
                if (magic is { } m && GetTypeByMagic(m) is { } typeByMagic) {
                    types.Add(typeByMagic);
                    this.MagicType = typeByMagic;
                }

                break;
        }

        this.PossibleTypes = types;
    }

    public IVirtualFile File { get; }

    /// <summary>Gets the lookup of <see cref="File"/>. It is owned by the caller; do not dispose.</summary>
    public IVirtualFileLookup Lookup { get; }

    public string Name { get; }

    public bool NameResolved { get; }

    /// <summary>Gets the lowercase extension including the dot, or an empty string if unknown.</summary>
    public string Extension { get; }

    public FileType PackType { get; }

    public long Size { get; }

    /// <summary>Gets the first 4 bytes of the file, if it has been read (only for standard files with unknown names).
    /// </summary>
    public uint? Magic { get; }

    /// <summary>Gets the file resource type identified from <see cref="Magic"/>, if any.</summary>
    public Type? MagicType { get; }

    /// <summary>Gets the possible file resource types, from the extension, pack type, and magic.</summary>
    public IReadOnlySet<Type> PossibleTypes { get; }

    public PlatformId PlatformId { get; }

    public ThumbnailSettings Settings { get; }

    public int Width => this.Settings.Width;

    public int Height => this.Settings.Height;

    /// <summary>Gets the full path in the file system, or null if not available.</summary>
    public string? FullPath => this._fullPath.Value;

    /// <summary>Gets the name to show; for files with unknown names, the hash.</summary>
    public string DisplayStem => this.NameResolved ? Path.GetFileNameWithoutExtension(this.Name) : this.Name;

    public bool IsPossibly<T>() where T : FileResource => this.PossibleTypes.Contains(typeof(T));

    public bool HasExtension(string extension) => this.Extension == extension;

    /// <summary>Tests whether the magic needs to be read to route a file to a provider; only files with unknown names
    /// are sniffed, as the extension is enough otherwise.</summary>
    public static bool ShouldReadMagic(IVirtualFile file, IVirtualFileLookup lookup) =>
        !file.NameResolved && lookup is { Type: FileType.Standard, Size: >= 4 };

    public static Type? GetTypeByMagic(uint magic) => magic switch {
        0x42444553u => typeof(ScdFile),
        0x46445845u => typeof(ExcelDataFile),
        0x46485845u => typeof(ExcelHeaderFile),
        0x544c5845u => typeof(ExcelListFile),
        ShcdHeader.MagicValue => typeof(ShcdFile),
        ShpkHeader.MagicValue => typeof(ShpkFile),
        SklbFile.MagicValue => typeof(SklbFile),
        AvfxFile.MagicValue => typeof(AvfxFile),
        _ => null,
    };

    private static Dictionary<string, Type> CreateTypeByExtensionMap()
    {
        var fileResourceType = typeof(FileResource);
        var res = new Dictionary<string, Type>();
        foreach (var assembly in new[] { typeof(FileResource).Assembly, typeof(SklbFile).Assembly }) {
            Type[] types;
            try {
                types = assembly.GetTypes();
            } catch (ReflectionTypeLoadException e) {
                types = e.Types.Where(x => x is not null).ToArray()!;
            }

            foreach (var type in types) {
                if (type == fileResourceType || !fileResourceType.IsAssignableFrom(type) || type.IsAbstract)
                    continue;
                var ext = type.GetCustomAttribute<FileExtensionAttribute>()?.Extension;
                if (ext is null && type.Name.EndsWith("File", StringComparison.Ordinal) && type.Name.Length > 4)
                    ext = $".{type.Name[..^4]}";
                if (ext is not null)
                    res.TryAdd(ext.ToLowerInvariant(), type);
            }
        }

        res[".atex"] = typeof(TexFile);
        return res;
    }
}

/// <summary>Settings that affect how thumbnails look. A change invalidates all thumbnails.</summary>
public sealed record ThumbnailSettings(
    int Width,
    int Height,
    InterpolationMode InterpolationMode,
    float CropThresholdAspectRatioRatio,
    float DpiScale);
