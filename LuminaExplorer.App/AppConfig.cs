using System;
using System.Drawing.Drawing2D;
using System.Text.Json.Serialization;
using LuminaExplorer.Core.ExcelSheets;
using LuminaExplorer.Core.GameDataNames;

namespace LuminaExplorer.App;

public record AppConfig {
    [JsonIgnore] public string BaseDirectory { get; init; } = "";

    public string PathListUrl { get; init; } = "https://rl2.perchbird.dev/download/export/PathList.gz";

    public string SqPackRootDirectoryPath { get; init; } =
        @"C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack";

    public string CacheFilePath { get; init; } = "paths.dat";

    /// <summary>Association between BNpcName and BNpcBase, used to name monster model folders.</summary>
    public string BNpcLinkUrl { get; init; } = CharaFolderNames.DefaultBNpcLinkUrl;

    public string BNpcLinkCacheFilePath { get; init; } = "bnpclink.csv";

    /// <summary>GitHub repository of EXDSchema, used to name the columns of Excel sheets.</summary>
    public string ExcelSchemaRepository { get; init; } = ExcelSchemaProvider.DefaultRepository;

    /// <summary>Directory to keep downloaded EXDSchema archives in.</summary>
    public string ExcelSchemaCacheDirectory { get; init; } = "exdschema";

    public int ListViewMode { get; init; } = 10; // Details

    public float CropThresholdAspectRatioRatio { get; init; } = 2;

    public int PreviewThumbnailMinimumKeepInMemoryEntries { get; init; } = 128;

    public float PreviewThumbnailMinimumKeepInMemoryPages { get; init; } = 4;

    public InterpolationMode PreviewInterpolationMode { get; init; } = InterpolationMode.Low;

    public int PreviewThumbnailerThreads { get; init; } = Math.Min(4, Environment.ProcessorCount);
    // public int PreviewThumbnailerThreads { get; init; } = Math.Max(1, Environment.ProcessorCount - 1);

    public TimeSpan SearchEntryTimeout { get; init; } = TimeSpan.FromSeconds(1);

    public int SearchThreads { get; init; } = Math.Max(1, Environment.ProcessorCount / 2);

    public int SortThreads { get; init; } = Math.Max(1, Environment.ProcessorCount - 1);

    public string LastFolder { get; init; } = "/";
}
