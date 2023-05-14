using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using LuminaExplorer.App.Window;
using LuminaExplorer.App.Window.FileViewers;
using LuminaExplorer.Core.ExtraFormats.GltfInterop;
using LuminaExplorer.Core.SqPackPath;
using LuminaExplorer.Core.VirtualFileSystem.Sqpack;

namespace LuminaExplorer.App;

public static class Program {
    private static void GetAppConfig(out AppConfig appConfig, out SqpackFileSystem fs) {
        var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

        AppConfig? appConfigTmp;
        try {
            using var f = File.OpenRead(Path.Combine(baseDir, "config.json"));
            appConfigTmp = JsonSerializer.Deserialize<AppConfig>(f);
        } catch (Exception) {
            using var f = File.Open(Path.Combine(baseDir, "config.json"), FileMode.Create, FileAccess.Write);
            JsonSerializer.Serialize(f, appConfigTmp = new());
        }

        appConfigTmp ??= new();

        var gameData = new Lumina.GameData(appConfigTmp.SqPackRootDirectoryPath);
        var hashCacheFile = new FileInfo(Path.Combine(baseDir, appConfigTmp.CacheFilePath));
        if (!hashCacheFile.Exists || hashCacheFile.Length == 0) {
            HashDatabase.MakeCachedFile(
                appConfigTmp.PathListUrl,
                hashCacheFile.OpenWrite(),
                x => Debug.WriteLine($@"Progress: {x * 100:0.00}%"),
                new()).Wait();
        }

        var hashdb = new HashDatabase(hashCacheFile);
        fs = new(hashdb, gameData);

        appConfig = appConfigTmp with {BaseDirectory = baseDir};
    }

    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    public static void Main() {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();

        switch (0) {
            case 0:
                Main_Explorer();
                return;
            case 1:
                Main_Show0361();
                return;
            case 2:
                Main_Import0361();
                return;
        }
    }

    public static void Main_Explorer() {
        GetAppConfig(out var appConfig, out var fs);

        using var mainExplorer = new Explorer(appConfig, fs);

        try {
            Application.Run(mainExplorer);
            appConfig = mainExplorer.AppConfig;
        } finally {
            using var f = File.Open(
                Path.Combine(appConfig.BaseDirectory, "config.json"),
                FileMode.Create,
                FileAccess.Write);
            JsonSerializer.Serialize(f, appConfig);
        }
    }

    [STAThread]
    public static void Main_Show0361() {
        GetAppConfig(out _, out var fs);
        var viewer = new ModelViewer {
            Size = new(1024, 768),
        };
        viewer.Load += (_, _) => Task
            .Run(() => fs.LocateFile(fs.RootFolder, "chara/monster/m0361/obj/body/b0001/model/m0361b0001.mdl"))
            .ContinueWith(
                r => viewer.SetFile(fs, fs.RootFolder, r.Result!, null),
                TaskScheduler.FromCurrentSynchronizationContext());
        Application.Run(viewer);
    }

    public static void Main_Import0361() => Task.Factory.StartNew(async () => {
        GetAppConfig(out _, out var fs);

        var tuple = new GltfTuple(File.OpenRead("Z:/m0361b0001.glb"));

        Debugger.Break();
    });
}
