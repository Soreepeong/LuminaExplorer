using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using LuminaExplorer.App.Window;
using LuminaExplorer.App.Window.FileViewers;
using LuminaExplorer.Core.SqPackPath;
using LuminaExplorer.Core.VirtualFileSystem.Physical;
using LuminaExplorer.Core.VirtualFileSystem.Shell;
using LuminaExplorer.Core.VirtualFileSystem.Sqpack;

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

        // using var tv = new TextureViewer();
        // Application.Run(tv);
        // return;

        var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

        AppConfig? appConfig;
        try {
            using var f = File.OpenRead(Path.Combine(baseDir, "config.json"));
            appConfig = JsonSerializer.Deserialize<AppConfig>(f);
        } catch (Exception) {
            using var f = File.Open(Path.Combine(baseDir, "config.json"), FileMode.Create, FileAccess.Write);
            JsonSerializer.Serialize(f, appConfig = new());
        }

        appConfig ??= new();

        /*
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
        using var fs = new SqpackFileSystem(hashdb, gameData);
        //*/
        // using var fs = new PhysicalFileSystem();
        using var fs = new ShellFileSystem();
        
        using var mainExplorer = new Explorer(appConfig, fs);
        
        try {
            Application.Run(mainExplorer);
            appConfig = mainExplorer.AppConfig;
        } finally {
            using var f = File.Open(Path.Combine(baseDir, "config.json"), FileMode.Create, FileAccess.Write);
            JsonSerializer.Serialize(f, appConfig);
        }
    }
}
