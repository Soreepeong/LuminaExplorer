using System.Drawing.Drawing2D;

namespace LuminaExplorer.App; 

public class AppConfig {
    public string SqPackRootDirectoryPath =
        @"C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack";

    public string CacheFilePath = "paths.dat";

    public float CropThresholdAspectRatioRatio = 2;

    public int PreviewThumbnailMinimumKeepInMemoryEntries = 128;

    public float PreviewThumbnailMinimumKeepInMemoryPages = 4;

    public InterpolationMode PreviewInterpolationMode = InterpolationMode.Low;

    public int PreviewThumbnailerThreads = Math.Min(4, Environment.ProcessorCount);
    // public int PreviewThumbnailerThreads = Math.Max(1, Environment.ProcessorCount - 1);

    public TimeSpan SearchEntryTimeout = TimeSpan.FromSeconds(1);

    public int SearchThreads = Math.Max(1, Environment.ProcessorCount / 2);
    
    public int SortThreads = Math.Max(1, Environment.ProcessorCount - 1);
}
