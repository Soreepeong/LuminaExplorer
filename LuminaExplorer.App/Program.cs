﻿using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Lumina.Data.Files;
using LuminaExplorer.App.Window;
using LuminaExplorer.App.Window.FileViewers;
using LuminaExplorer.Controls.FileResourceViewerControls.ModelViewerControl;
using LuminaExplorer.Core.SqPackPath;
using LuminaExplorer.Core.VirtualFileSystem.Physical;
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

        //*
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
        /*/
        using var fs = new PhysicalFileSystem();
        //*/
        {
            var viewer = new Form {
                Size = new(1024, 768)
            };
            Task.Delay(100).ContinueWith(_ => 
            viewer.BeginInvoke(() => {
                var t = Task.Run(async () => {
                    var ft = fs.FindFile(fs.RootFolder, "chara/monster/m0361/obj/body/b0001/model/m0361b0001.mdl");
                    await ft;
                    using var l = fs.GetLookup(ft.Result);
                    return await l.AsFileResource<MdlFile>();
                });
                t.Wait();
                var c = new ModelViewerControl {Dock = DockStyle.Fill};
                c.SetModel(fs, fs.RootFolder, t.Result);
                viewer.Controls.Add(c);
            }));
            Application.Run(viewer);
            return;
        }
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
