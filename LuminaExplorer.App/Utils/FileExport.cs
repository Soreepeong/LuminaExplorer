using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Data.Structs;
using Lumina.Models.Materials;
using LuminaExplorer.Controls.FileResourceViewerControls.ScdPlayerControl;
using LuminaExplorer.Core.Audio;
using LuminaExplorer.Core.ExcelSheets;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra;
using LuminaExplorer.Core.ExtraFormats.GltfInterop;
using LuminaExplorer.Core.Util;
using LuminaExplorer.Core.VirtualFileSystem;
using TerraFX.Interop.Windows;

namespace LuminaExplorer.App.Utils;

/// <summary>How files are written when exported.</summary>
public enum FileExportMode {
    /// <summary>The file as stored in the game.</summary>
    AsIs,

    /// <summary>Converted into the recommended format if there is one (see
    /// <see cref="FileExport.GetRecommendedExtension(FileConversionKind)"/>), or as-is otherwise.</summary>
    Recommended,

    /// <summary>Textures as DirectDraw Surface files; other files as-is.</summary>
    Dds,

    /// <summary>Sounds as their native streams (Ogg Vorbis as .ogg; ADPCM and PCM as .wav); other files as-is.
    /// </summary>
    NativeAudio,
}

/// <summary>Kinds of files that can be converted.</summary>
public enum FileConversionKind {
    None,
    Texture,
    Model,
    Sound,
    ExcelSheet,
}

/// <summary>Shared state of an export operation.</summary>
public sealed class FileExportContext {
    private Task<ModelInfoResolver>? _modelInfoResolver;
    private Task<ExcelLinkResolver?>? _excelLinkResolver;

    public FileExportContext(IVirtualFileSystem vfs, ExcelSchemaProvider? excelSchemaProvider)
    {
        this.Vfs = vfs;
        this.ExcelSchemaProvider = excelSchemaProvider;
    }

    public IVirtualFileSystem Vfs { get; }

    public ExcelSchemaProvider? ExcelSchemaProvider { get; }

    /// <summary>Gets the resolver of the skeletons of models.</summary>
    public Task<ModelInfoResolver> GetModelInfoResolver() =>
        LazyInitializer.EnsureInitialized(
            ref this._modelInfoResolver,
            () => Task.Run(
                () => ModelInfoResolver.GetResolver(
                    x => this.TryLoadAsync<EstFile>(x, default),
                    x => this.TryLoadAsync<PbdFile>(x, default))));

    /// <summary>Gets the resolver of links between Excel sheets, shared by the sheets exported together.</summary>
    /// <returns>The resolver, or <c>null</c> if no schema is available.</returns>
    public Task<ExcelLinkResolver?> GetExcelLinkResolver() =>
        LazyInitializer.EnsureInitialized(
            ref this._excelLinkResolver,
            () => Task.Run(
                async () => this.ExcelSchemaProvider is { } provider &&
                    (await provider.GetSchemaSetAsync()).Set is { } set
                        ? new ExcelLinkResolver(this.Vfs, this.Vfs.RootFolder, set)
                        : null));

    /// <summary>Loads a file by its game path.</summary>
    /// <returns>The file, or <c>null</c> if it does not exist or could not be loaded.</returns>
    public async Task<T?> TryLoadAsync<T>(string path, CancellationToken cancellationToken) where T : FileResource
    {
        try {
            if (await this.Vfs.LocateFile(this.Vfs.RootFolder, path) is not { } file)
                return null;
            using var lookup = this.Vfs.GetLookup(file);
            return await lookup.AsFileResource<T>(cancellationToken);
        } catch (Exception e) when (e is not OperationCanceledException) {
            return null;
        }
    }
}

/// <summary>A file to export, and the directory to put it in, relative to the destination.</summary>
/// <param name="File">The file.</param>
/// <param name="RelativeDirectory">Directory relative to the destination; empty, or ending with a slash.</param>
public sealed record FileExportSource(IVirtualFile File, string RelativeDirectory) {
    public string RelativePath => this.RelativeDirectory + FileExport.SanitizeFileName(this.File.Name);
}

/// <summary>A file to be written by an export.</summary>
public sealed class FileExportOutput {
    public FileExportOutput(
        FileExportSource source,
        string baseName,
        string suffix,
        string extension,
        long? length,
        Func<Stream, CancellationToken, Task> writeAsync)
    {
        this.Source = source;
        this.BaseName = baseName;
        this.Suffix = suffix;
        this.Extension = extension;
        this.Length = length;
        this.WriteAsync = writeAsync;
    }

    public FileExportSource Source { get; }

    /// <summary>Gets the name of the source file without its extension.</summary>
    public string BaseName { get; }

    /// <summary>Gets the suffix, for sources that are written as multiple files (e.g. <c>.1</c>).</summary>
    public string Suffix { get; }

    /// <summary>Gets the extension, including the dot; may be empty.</summary>
    public string Extension { get; }

    /// <summary>Gets or sets the text inserted before the extension to make the name unique (e.g. <c> (2)</c>).
    /// </summary>
    public string Disambiguator { get; set; } = string.Empty;

    /// <summary>Gets the size of the file, if known in advance.</summary>
    public long? Length { get; }

    /// <summary>Gets the function that writes the file. The stream must be seekable.</summary>
    public Func<Stream, CancellationToken, Task> WriteAsync { get; }

    public string FileName => this.BaseName + this.Suffix + this.Disambiguator + this.Extension;

    /// <summary>Gets the path relative to the destination, using slashes.</summary>
    public string RelativePath => this.Source.RelativeDirectory + this.FileName;
}

/// <summary>Converts and exports files of a virtual file system.</summary>
public static class FileExport {
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    /// <summary>Gets what a file can be converted to, judging from its name.</summary>
    public static FileConversionKind GetConversionKind(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch {
            ".tex" or ".atex" => FileConversionKind.Texture,
            ".mdl" => FileConversionKind.Model,
            ".scd" => FileConversionKind.Sound,
            ExcelSheetFileNames.HeaderExtension or ExcelSheetFileNames.DataExtension => FileConversionKind.ExcelSheet,
            _ => FileConversionKind.None,
        };

    /// <summary>Gets the extension of the recommended format, including the dot.</summary>
    public static string? GetRecommendedExtension(FileConversionKind kind) => kind switch {
        FileConversionKind.Texture => ".png",
        FileConversionKind.Model => ".glb",
        FileConversionKind.Sound => ".wav",
        FileConversionKind.ExcelSheet => ".csv",
        _ => null,
    };

    /// <summary>Gets the extension of the recommended format of a file, judging from its name.</summary>
    public static string? GetRecommendedExtension(string fileName) =>
        GetRecommendedExtension(GetConversionKind(fileName));

    /// <summary>Guesses the extension of the file written for a file in the given mode, from its name.</summary>
    /// <returns>Extension including the dot; empty if unknown.</returns>
    public static string GetExpectedExtension(string fileName, FileExportMode mode)
    {
        var kind = GetConversionKind(fileName);
        return mode switch {
            FileExportMode.Recommended when GetRecommendedExtension(kind) is { } ext => ext,
            FileExportMode.Dds when kind == FileConversionKind.Texture => ".dds",
            FileExportMode.NativeAudio when kind == FileConversionKind.Sound => ".ogg",
            _ => Path.GetExtension(fileName),
        };
    }

    /// <summary>Replaces characters that cannot be used in file names.</summary>
    public static string SanitizeFileName(string name)
    {
        name = name.Replace("\0", null).Trim('/');
        if (name.IndexOfAny(InvalidFileNameChars) != -1)
            name = string.Concat(name.Select(x => Array.IndexOf(InvalidFileNameChars, x) == -1 ? x : '_'));
        return name.Trim() is "" or "." or ".." ? "_" : name;
    }

    /// <summary>Lists the given files, and the files in the given folders and their subfolders.</summary>
    /// <param name="vfs">Virtual file system.</param>
    /// <param name="files">Files.</param>
    /// <param name="folders">Folders; their files are placed in directories named after them.</param>
    /// <param name="progress">Called with the number of files found so far.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<List<FileExportSource>> EnumerateAsync(
        IVirtualFileSystem vfs,
        IReadOnlyList<IVirtualFile> files,
        IReadOnlyList<IVirtualFolder> folders,
        Action<int>? progress,
        CancellationToken cancellationToken)
    {
        var result = files.Select(x => new FileExportSource(x, string.Empty)).ToList();
        var visited = new HashSet<IVirtualFolder>();
        foreach (var folder in folders)
            await Traverse(folder, SanitizeFileName(folder.Name) + "/");
        return result;

        async Task Traverse(IVirtualFolder folder, string relativeDirectory)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(folder))
                return;

            await vfs.AsFoldersResolved(folder).WaitAsync(cancellationToken);
            await vfs.AsFileNamesResolved(folder).WaitAsync(cancellationToken);
            result.AddRange(
                vfs.GetFiles(folder)
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new FileExportSource(x, relativeDirectory)));
            progress?.Invoke(result.Count);

            foreach (var subfolder in vfs.GetFolders(folder).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)) {
                if (!Equals(subfolder, folder.Parent))
                    await Traverse(subfolder, relativeDirectory + SanitizeFileName(subfolder.Name) + "/");
            }
        }
    }

    /// <summary>Determines the files to write for a file.</summary>
    /// <param name="context">Export context.</param>
    /// <param name="source">File to export.</param>
    /// <param name="mode">Export mode.</param>
    /// <param name="keepLoaded">Whether the returned outputs may keep the file loaded while planning, so that
    /// they do not have to load it again. Use <c>false</c> if the outputs are kept for a long time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<List<FileExportOutput>> PlanAsync(
        FileExportContext context,
        FileExportSource source,
        FileExportMode mode,
        bool keepLoaded,
        CancellationToken cancellationToken)
    {
        var vfs = context.Vfs;
        var file = source.File;
        var name = SanitizeFileName(file.Name);
        var baseName = Path.GetFileNameWithoutExtension(name);
        var kind = GetConversionKind(name);

        if (mode != FileExportMode.AsIs && kind == FileConversionKind.None && !file.NameResolved) {
            // The type of files with unknown names can still be told for textures and models.
            using var lookup = vfs.GetLookup(file);
            kind = lookup.Type switch {
                FileType.Texture => FileConversionKind.Texture,
                FileType.Model => FileConversionKind.Model,
                _ => kind,
            };
        }

        switch (mode) {
            case FileExportMode.Recommended when kind == FileConversionKind.Texture: {
                var tex = await LoadAsync<TexFile>(vfs, file, cancellationToken);
                var slices = Math.Max(1, tex.TextureBuffer.DepthOfMipmap(0));
                Func<CancellationToken, Task<TexFile>> getTex = keepLoaded
                    ? _ => Task.FromResult(tex)
                    : ct => LoadAsync<TexFile>(vfs, file, ct);
                return Enumerable.Range(0, slices)
                    .Select(
                        slice => new FileExportOutput(
                            source,
                            baseName,
                            slices == 1 ? string.Empty : $".{slice}",
                            ".png",
                            null,
                            async (stream, ct) => {
                                var t = await getTex(ct);
                                ct.ThrowIfCancellationRequested();
                                using var wic = t.ToWicBitmapSource(0, slice);
                                wic.Save(stream, GUID.GUID_ContainerFormatPng);
                            }))
                    .ToList();
            }

            case FileExportMode.Recommended when kind == FileConversionKind.Model:
                return [
                    new(source, baseName, string.Empty, ".glb", null, (s, ct) => WriteGlbAsync(context, file, s, ct)),
                ];

            case FileExportMode.Recommended when kind == FileConversionKind.ExcelSheet:
                return [
                    new(
                        source,
                        baseName,
                        string.Empty,
                        ".csv",
                        null,
                        async (s, ct) => await ExcelCsvExporter.ExportAsync(
                            vfs,
                            vfs.RootFolder,
                            file,
                            context.ExcelSchemaProvider,
                            s,
                            ct,
                            await context.GetExcelLinkResolver())),
                ];

            case FileExportMode.Recommended or FileExportMode.NativeAudio when kind == FileConversionKind.Sound: {
                var native = mode == FileExportMode.NativeAudio;
                var entries = await LoadScdEntriesAsync(vfs, file, cancellationToken);
                var exportable = entries.Where(x => native ? x.NativeFileExtension is not null : x.IsPlayable)
                    .ToArray();
                if (exportable.Length == 0) {
                    throw new NotSupportedException(
                        entries.Count == 0
                            ? "The file contains no sounds."
                            : entries.First().UnsupportedReason ?? "The sounds cannot be exported.");
                }

                Func<int, CancellationToken, Task<ScdAudioEntry>> getEntry = keepLoaded
                    ? (index, _) => Task.FromResult(entries.First(x => x.Index == index))
                    : async (index, ct) => (await LoadScdEntriesAsync(vfs, file, ct)).First(x => x.Index == index);
                return exportable
                    .Select(
                        entry => new FileExportOutput(
                            source,
                            baseName,
                            entries.Count == 1 ? string.Empty : $".{entry.Index}",
                            native ? entry.NativeFileExtension! : ".wav",
                            null,
                            async (stream, ct) => {
                                var e = await getEntry(entry.Index, ct);
                                if (native)
                                    e.WriteNativeFile(stream);
                                else
                                    ScdAudioExport.WriteDecodedWav(e, stream, ct);
                            }))
                    .ToList();
            }

            case FileExportMode.Dds when kind == FileConversionKind.Texture:
                return [
                    new(
                        source,
                        baseName,
                        string.Empty,
                        ".dds",
                        null,
                        async (stream, ct) => {
                            var tex = await LoadAsync<TexFile>(vfs, file, ct);
                            using var dds = tex.ToDdsFile().CreateStream();
                            await dds.CopyToAsync(stream, ct);
                        }),
                ];

            default: {
                long size;
                using (var lookup = vfs.GetLookup(file))
                    size = lookup.Size;
                return [
                    new(
                        source,
                        baseName,
                        string.Empty,
                        Path.GetExtension(name),
                        size,
                        (stream, ct) => {
                            using var lookup = vfs.GetLookup(file);
                            using var src = lookup.CreateStream();
                            var buffer = new byte[81920];
                            int read;
                            while ((read = src.Read(buffer, 0, buffer.Length)) > 0) {
                                ct.ThrowIfCancellationRequested();
                                stream.Write(buffer, 0, read);
                            }

                            return Task.CompletedTask;
                        }),
                ];
            }
        }
    }

    /// <summary>Makes the relative paths of the outputs unique, by adding <c> (2)</c> and so on to the later ones.
    /// </summary>
    public static void MakeUnique(IEnumerable<FileExportOutput> outputs, ISet<string> usedPaths)
    {
        foreach (var output in outputs) {
            for (var i = 2; !usedPaths.Add(output.RelativePath); i++)
                output.Disambiguator = $" ({i})";
        }
    }

    /// <summary>Writes a model as a binary glTF file, with its materials, textures, and skeleton if found.</summary>
    public static async Task WriteGlbAsync(
        FileExportContext context,
        IVirtualFile file,
        Stream stream,
        CancellationToken cancellationToken)
    {
        var vfs = context.Vfs;
        var mdl = await LoadAsync<MdlFile>(vfs, file, cancellationToken);
        var model = PenumbraMdlFile.CreateModel(mdl);
        var tuple = new GltfTuple();

        int? skinIndex = null;
        try {
            var resolver = await context.GetModelInfoResolver();
            var sklbs = new List<SklbFile>();
            foreach (var sklbPath in resolver.FindSklbPath(vfs.GetFullPath(file).TrimStart('/'))) {
                if (await context.TryLoadAsync<SklbFile>(sklbPath, cancellationToken) is { } sklb)
                    sklbs.Add(sklb);
            }

            if (sklbs.Count > 0)
                skinIndex = tuple.AttachSkin(sklbs.ToArray());
        } catch (Exception e) when (e is not OperationCanceledException) {
            // Export without the skeleton.
            tuple = new();
            skinIndex = null;
        }

        for (var i = 0; i < model.Materials.Length; i++) {
            cancellationToken.ThrowIfCancellationRequested();
            var mtrlPath = model.File!.Strings
                .AsSpan((int) model.File.MaterialNameOffsets[i])
                .ExtractCString();

            if (mtrlPath.StartsWith('/'))
                mtrlPath = Material.ResolveRelativeMaterialPath(mtrlPath, model.VariantId, strictSuffixValidation: false);

            if (mtrlPath is null)
                continue;

            if (await context.TryLoadAsync<MtrlFile>(mtrlPath, cancellationToken) is not { } mtrl)
                continue;

            model.Materials[i] = PenumbraMtrlFile.CreateMaterial(mtrl);
            typeof(Material).GetProperty(nameof(Material.MaterialPath))!.SetValue(
                model.Materials[i],
                mtrlPath.Trim('/'));
            typeof(Material).GetProperty(nameof(Material.ResolvedPath))!.SetValue(
                model.Materials[i],
                mtrlPath.Trim('/'));
            typeof(Material).GetProperty(nameof(Material.Parent))!.SetValue(model.Materials[i], model);
        }

        foreach (var m in model.Materials) {
            cancellationToken.ThrowIfCancellationRequested();
            await tuple.AttachMaterial(m, texPath => context.TryLoadAsync<TexFile>(texPath, cancellationToken));
        }

        tuple.AddToScene(tuple.AttachMesh(model, skinIndex), skinIndex);
        tuple.Compile(stream);
    }

    private static async Task<T> LoadAsync<T>(
        IVirtualFileSystem vfs,
        IVirtualFile file,
        CancellationToken cancellationToken) where T : FileResource
    {
        using var lookup = vfs.GetLookup(file);
        return await lookup.AsFileResource<T>(cancellationToken);
    }

    private static async Task<List<ScdAudioEntry>> LoadScdEntriesAsync(
        IVirtualFileSystem vfs,
        IVirtualFile file,
        CancellationToken cancellationToken)
    {
        var scd = await LoadAsync<ScdFile>(vfs, file, cancellationToken);
        return ScdAudioEntry.ReadAll(scd, cancellationToken);
    }
}

/// <summary>Progress of an export, updated from any thread.</summary>
public sealed class FileExportProgress {
    private int _done;
    private int _total = -1;
    private int _filesWritten;

    /// <summary>Gets the number of processed source files.</summary>
    public int Done => Volatile.Read(ref this._done);

    /// <summary>Gets the number of source files; -1 while still listing them.</summary>
    public int Total => Volatile.Read(ref this._total);

    /// <summary>Gets the number of files written.</summary>
    public int FilesWritten => Volatile.Read(ref this._filesWritten);

    /// <summary>Gets or sets the text describing the current state.</summary>
    public string Status { get; set; } = string.Empty;

    public ConcurrentQueue<(string Path, string Message)> Errors { get; } = new();

    public void SetTotal(int total) => Volatile.Write(ref this._total, total);

    public void IncrementDone() => Interlocked.Increment(ref this._done);

    public void IncrementFilesWritten() => Interlocked.Increment(ref this._filesWritten);
}
