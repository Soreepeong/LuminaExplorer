using System;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles; 

public interface IShaderEntry {
    public ShaderHeader Header { get; }
    public ShaderInput[] InputTables { get; }
    public string[] InputNames { get; }
    public ReadOnlySpan<byte> ByteCode { get; }
    public ShaderType ShaderType { get; }
}
