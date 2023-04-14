using System.Diagnostics;
using System.Reflection;
using LuminaExplorer.SqPackPath;
using LuminaExplorer.Window;

namespace LuminaExplorer;

static class Program {
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main() {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();

        var gameData = new Lumina.GameData(@"C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack");
        var hashCacheFile = new FileInfo(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "paths.dat"));
        if (!hashCacheFile.Exists || hashCacheFile.Length == 0)
            HashDatabase.WriteCachedFile(hashCacheFile.OpenWrite(), x => Debug.WriteLine($@"Progress: {x * 100:0.00}%"), new()).Wait();
        var hashdb = new HashDatabase(hashCacheFile);

        Application.Run(new Explorer(hashdb, gameData));
    }
}
