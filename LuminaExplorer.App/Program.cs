using System.Diagnostics;
using System.Reflection;
using LuminaExplorer.App.Window;
using LuminaExplorer.Core.LazySqPackTree;
using LuminaExplorer.Core.SqPackPath;

namespace LuminaExplorer.App;

static class Program {
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main() {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();

        var appConfig = new AppConfig();
        var gameData = new Lumina.GameData(appConfig.SqPackRootDirectoryPath);
        var hashCacheFile = new FileInfo(Path.Combine(
            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
                appConfig.CacheFilePath)));
        if (!hashCacheFile.Exists || hashCacheFile.Length == 0)
            HashDatabase.WriteCachedFile(hashCacheFile.OpenWrite(), x => Debug.WriteLine($@"Progress: {x * 100:0.00}%"),
                new()).Wait();
        var hashdb = new HashDatabase(hashCacheFile);

        using var vsptree = new VirtualSqPackTree(hashdb, gameData);
        using var mainExplorer = new Explorer();
        mainExplorer.AppConfig = appConfig;
        mainExplorer.Tree = vsptree;
        Application.Run(mainExplorer);
    }
}
