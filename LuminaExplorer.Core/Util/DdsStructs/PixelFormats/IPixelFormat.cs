namespace LuminaExplorer.Core.Util.DdsStructs.PixelFormats;

public interface IPixelFormat {
    DxgiFormat DxgiFormat => PixelFormatResolver.GetDxgiFormat(this);
    DdsFourCc FourCc => PixelFormatResolver.GetFourCc(this);
}