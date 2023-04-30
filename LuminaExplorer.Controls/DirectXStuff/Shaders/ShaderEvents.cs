using System.Threading.Tasks;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders;

public static class ShaderEvents {
    /// <summary>
    /// Requests callee to make a task.
    ///
    /// It may return null to use empty value.
    /// If it is left as null, then it will be requested again on next draw pass.
    /// </summary>
    public delegate void FileRequested<T>(string path, ref Task<T?>? loader);
}
