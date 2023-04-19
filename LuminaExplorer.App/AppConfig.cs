using System.Drawing.Drawing2D;

namespace LuminaExplorer.App;

public class AppConfig {
    public string PathListUrl { get; set; } = "https://rl2.perchbird.dev/download/export/PathList.gz";
    
    public string SqPackRootDirectoryPath { get; set; } =
        @"C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack";

    public string CacheFilePath { get; set; } = "paths.dat";

    public float CropThresholdAspectRatioRatio { get; set; } = 2;

    public int PreviewThumbnailMinimumKeepInMemoryEntries { get; set; } = 128;

    public float PreviewThumbnailMinimumKeepInMemoryPages { get; set; } = 4;

    public InterpolationMode PreviewInterpolationMode { get; set; } = InterpolationMode.Low;

    public int PreviewThumbnailerThreads { get; set; } = Math.Min(4, Environment.ProcessorCount);
    // public int PreviewThumbnailerThreads { get; set; } = Math.Max(1, Environment.ProcessorCount - 1);

    public TimeSpan SearchEntryTimeout { get; set; } = TimeSpan.FromSeconds(1);

    public int SearchThreads { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);

    public int SortThreads { get; set; } = Math.Max(1, Environment.ProcessorCount - 1);
}
