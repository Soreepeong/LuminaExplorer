using System.Drawing.Drawing2D;

namespace LuminaExplorer.App;

public record AppConfig {
    public string PathListUrl { get; init; } = "https://rl2.perchbird.dev/download/export/PathList.gz";
    
    public string SqPackRootDirectoryPath { get; init; } =
        @"C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack";

    public string CacheFilePath { get; init; } = "paths.dat";

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
