namespace LuminaExplorer.Core.Util.DdsStructs.PixelFormats;

public readonly struct UnknownPixelFormat : IPixelFormat {
    public static readonly UnknownPixelFormat Instance = new();
    
    public DxgiFormat DxgiFormat => DxgiFormat.Unknown;
    public DdsFourCc FourCc => DdsFourCc.Unknown;
}