using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
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

        var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

        AppConfig? appConfig;
        try {
            using var f = File.OpenRead(Path.Combine(baseDir, "config.json"));
            appConfig = JsonSerializer.Deserialize<AppConfig>(f);
        } catch (Exception) {
            using var f = File.OpenWrite(Path.Combine(baseDir, "config.template.json"));
            JsonSerializer.Serialize(f, appConfig = new());
        }

        appConfig ??= new();

        var gameData = new Lumina.GameData(appConfig.SqPackRootDirectoryPath);
        var hashCacheFile = new FileInfo(Path.Combine(baseDir, appConfig.CacheFilePath));
        if (!hashCacheFile.Exists || hashCacheFile.Length == 0) {
            HashDatabase.MakeCachedFile(
                appConfig.PathListUrl,
                hashCacheFile.OpenWrite(),
                x => Debug.WriteLine($@"Progress: {x * 100:0.00}%"),
                new()).Wait();
        }

        var hashdb = new HashDatabase(hashCacheFile);

        using var vsptree = new VirtualSqPackTree(hashdb, gameData);
        using var mainExplorer = new Explorer();
        mainExplorer.AppConfig = appConfig;
        mainExplorer.Tree = vsptree;
        Application.Run(mainExplorer);
    }
}
