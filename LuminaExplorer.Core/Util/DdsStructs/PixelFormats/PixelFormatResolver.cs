using System.Collections.Generic;
using System.Linq;

namespace LuminaExplorer.Core.Util.DdsStructs.PixelFormats; 

public static class PixelFormatResolver {
    public static readonly IReadOnlyDictionary<DdsFourCc, IPixelFormat> FourCcToPixelFormat;
    public static readonly IReadOnlyDictionary<DxgiFormat, IPixelFormat> DxgiFormatToPixelFormat;

    static PixelFormatResolver() {
        FourCcToPixelFormat = new Dictionary<DdsFourCc, IPixelFormat> {
            {DdsFourCc.Dxt1, new BcPixelFormat(ValueType.Unorm, AlphaType.Straight, 1)},
            {DdsFourCc.Dxt2, new BcPixelFormat(ValueType.Unorm, AlphaType.Premultiplied, 2)},
            {DdsFourCc.Dxt3, new BcPixelFormat(ValueType.Unorm, AlphaType.Straight, 2)},
            {DdsFourCc.Dxt4, new BcPixelFormat(ValueType.Unorm, AlphaType.Premultiplied, 3)},
            {DdsFourCc.Dxt5, new BcPixelFormat(ValueType.Unorm, AlphaType.Straight, 3)},
            {DdsFourCc.Bc4, new BcPixelFormat(ValueType.Unorm, AlphaType.Straight, 4)},
            {DdsFourCc.Bc4U, new BcPixelFormat(ValueType.Unorm, AlphaType.Straight, 4)},
            {DdsFourCc.Bc4S, new BcPixelFormat(ValueType.Snorm, AlphaType.Straight, 4)},
            {DdsFourCc.Bc5, new BcPixelFormat(ValueType.Unorm, AlphaType.Straight, 5)},
            {DdsFourCc.Bc5U, new BcPixelFormat(ValueType.Unorm, AlphaType.Straight, 5)},
            {DdsFourCc.Bc5S, new BcPixelFormat(ValueType.Snorm, AlphaType.Straight, 5)},
        };
        DxgiFormatToPixelFormat = new Dictionary<DxgiFormat, IPixelFormat> {
            {DxgiFormat.R32G32B32A32Typeless, RgbaxPixelFormat.CreateRgba(ValueType.Typeless, 32, 32, 32, 32)},
            {DxgiFormat.R32G32B32A32Float, RgbaxPixelFormat.CreateRgba(ValueType.Float, 32, 32, 32, 32)},
            {DxgiFormat.R32G32B32A32Uint, RgbaxPixelFormat.CreateRgba(ValueType.Uint, 32, 32, 32, 32)},
            {DxgiFormat.R32G32B32A32Sint, RgbaxPixelFormat.CreateRgba(ValueType.Sint, 32, 32, 32, 32)},
            {DxgiFormat.R32G32B32Typeless, RgbaxPixelFormat.CreateRgb(ValueType.Typeless, 32, 32, 32)},
            {DxgiFormat.R32G32B32Float, RgbaxPixelFormat.CreateRgb(ValueType.Float, 32, 32, 32)},
            {DxgiFormat.R32G32B32Uint, RgbaxPixelFormat.CreateRgb(ValueType.Uint, 32, 32, 32)},
            {DxgiFormat.R32G32B32Sint, RgbaxPixelFormat.CreateRgb(ValueType.Sint, 32, 32, 32)},
            {DxgiFormat.R16G16B16A16Typeless, RgbaxPixelFormat.CreateRgba(ValueType.Typeless, 16, 16, 16, 16)},
            {DxgiFormat.R16G16B16A16Float, RgbaxPixelFormat.CreateRgba(ValueType.Float, 16, 16, 16, 16)},
            {DxgiFormat.R16G16B16A16Unorm, RgbaxPixelFormat.CreateRgba(ValueType.Unorm, 16, 16, 16, 16)},
            {DxgiFormat.R16G16B16A16Uint, RgbaxPixelFormat.CreateRgba(ValueType.Uint, 16, 16, 16, 16)},
            {DxgiFormat.R16G16B16A16Snorm, RgbaxPixelFormat.CreateRgba(ValueType.Snorm, 16, 16, 16, 16)},
            {DxgiFormat.R16G16B16A16Sint, RgbaxPixelFormat.CreateRgba(ValueType.Sint, 16, 16, 16, 16)},
            {DxgiFormat.R32G32Typeless, RgbaxPixelFormat.CreateRg(ValueType.Typeless, 32, 32)},
            {DxgiFormat.R32G32Float, RgbaxPixelFormat.CreateRg(ValueType.Float, 32, 32)},
            {DxgiFormat.R32G32Uint, RgbaxPixelFormat.CreateRg(ValueType.Uint, 32, 32)},
            {DxgiFormat.R32G32Sint, RgbaxPixelFormat.CreateRg(ValueType.Sint, 32, 32)},
            {DxgiFormat.R10G10B10A2Typeless, RgbaxPixelFormat.CreateRgba(ValueType.Typeless, 10, 10, 10, 2)},
            {DxgiFormat.R10G10B10A2Unorm, RgbaxPixelFormat.CreateRgba(ValueType.Unorm, 10, 10, 10, 2)},
            {DxgiFormat.R10G10B10A2Uint, RgbaxPixelFormat.CreateRgba(ValueType.Uint, 10, 10, 10, 2)},
            {DxgiFormat.R8G8B8A8Typeless, RgbaxPixelFormat.CreateRgba(ValueType.Typeless, 8, 8, 8, 8)},
            {DxgiFormat.R8G8B8A8Unorm, RgbaxPixelFormat.CreateRgba(ValueType.Unorm, 8, 8, 8, 8)},
            {DxgiFormat.R8G8B8A8UnormSrgb, RgbaxPixelFormat.CreateRgba(ValueType.UnormSrgb, 8, 8, 8, 8)},
            {DxgiFormat.R8G8B8A8Uint, RgbaxPixelFormat.CreateRgba(ValueType.Uint, 8, 8, 8, 8)},
            {DxgiFormat.R8G8B8A8Snorm, RgbaxPixelFormat.CreateRgba(ValueType.Snorm, 8, 8, 8, 8)},
            {DxgiFormat.R8G8B8A8Sint, RgbaxPixelFormat.CreateRgba(ValueType.Sint, 8, 8, 8, 8)},
            {DxgiFormat.R16G16Typeless, RgbaxPixelFormat.CreateRg(ValueType.Typeless, 16, 16)},
            {DxgiFormat.R16G16Float, RgbaxPixelFormat.CreateRg(ValueType.Float, 16, 16)},
            {DxgiFormat.R16G16Unorm, RgbaxPixelFormat.CreateRg(ValueType.Unorm, 16, 16)},
            {DxgiFormat.R16G16Uint, RgbaxPixelFormat.CreateRg(ValueType.Uint, 16, 16)},
            {DxgiFormat.R16G16Snorm, RgbaxPixelFormat.CreateRg(ValueType.Snorm, 16, 16)},
            {DxgiFormat.R16G16Sint, RgbaxPixelFormat.CreateRg(ValueType.Sint, 16, 16)},
            {DxgiFormat.R32Typeless, RgbaxPixelFormat.CreateR(ValueType.Typeless, 32)},
            {DxgiFormat.R32Float, RgbaxPixelFormat.CreateR(ValueType.Float, 32)},
            {DxgiFormat.R32Uint, RgbaxPixelFormat.CreateR(ValueType.Uint, 32)},
            {DxgiFormat.R32Sint, RgbaxPixelFormat.CreateR(ValueType.Sint, 32)},
            {DxgiFormat.R24G8Typeless, RgbaxPixelFormat.CreateRg(ValueType.Typeless, 24, 8)},
            {DxgiFormat.R8G8Typeless, RgbaxPixelFormat.CreateRg(ValueType.Typeless, 8, 8)},
            {DxgiFormat.R8G8Unorm, RgbaxPixelFormat.CreateRg(ValueType.Unorm, 8, 8)},
            {DxgiFormat.R8G8Uint, RgbaxPixelFormat.CreateRg(ValueType.Uint, 8, 8)},
            {DxgiFormat.R8G8Snorm, RgbaxPixelFormat.CreateRg(ValueType.Snorm, 8, 8)},
            {DxgiFormat.R8G8Sint, RgbaxPixelFormat.CreateRg(ValueType.Sint, 8, 8)},
            {DxgiFormat.R16Typeless, RgbaxPixelFormat.CreateR(ValueType.Typeless, 16)},
            {DxgiFormat.R16Float, RgbaxPixelFormat.CreateR(ValueType.Float, 16)},
            {DxgiFormat.R16Unorm, RgbaxPixelFormat.CreateR(ValueType.Unorm, 16)},
            {DxgiFormat.R16Uint, RgbaxPixelFormat.CreateR(ValueType.Uint, 16)},
            {DxgiFormat.R16Snorm, RgbaxPixelFormat.CreateR(ValueType.Snorm, 16)},
            {DxgiFormat.R16Sint, RgbaxPixelFormat.CreateR(ValueType.Sint, 16)},
            {DxgiFormat.R8Typeless, RgbaxPixelFormat.CreateR(ValueType.Typeless, 8)},
            {DxgiFormat.R8Unorm, RgbaxPixelFormat.CreateR(ValueType.Float, 8)},
            {DxgiFormat.R8Uint, RgbaxPixelFormat.CreateR(ValueType.Unorm, 8)},
            {DxgiFormat.R8Snorm, RgbaxPixelFormat.CreateR(ValueType.Uint, 8)},
            {DxgiFormat.R8Sint, RgbaxPixelFormat.CreateR(ValueType.Sint, 8)},
            {DxgiFormat.A8Unorm, RgbaxPixelFormat.CreateA(ValueType.Unorm, 8)},
            {DxgiFormat.Bc1Typeless, new BcPixelFormat(ValueType.Typeless, AlphaType.Straight, 1)},
            {DxgiFormat.Bc1Unorm, new BcPixelFormat(ValueType.Unorm, AlphaType.Straight, 1)},
            {DxgiFormat.Bc1UnormSrgb, new BcPixelFormat(ValueType.UnormSrgb, AlphaType.Straight, 1)},
            {DxgiFormat.Bc2Typeless, new BcPixelFormat(ValueType.Typeless, AlphaType.Straight, 2)},
            {DxgiFormat.Bc2Unorm, new BcPixelFormat(ValueType.Unorm, AlphaType.Straight, 2)},
            {DxgiFormat.Bc2UnormSrgb, new BcPixelFormat(ValueType.UnormSrgb, AlphaType.Straight, 2)},
            {DxgiFormat.Bc3Typeless, new BcPixelFormat(ValueType.Typeless, AlphaType.Straight, 3)},
            {DxgiFormat.Bc3Unorm, new BcPixelFormat(ValueType.Unorm, AlphaType.Straight, 3)},
            {DxgiFormat.Bc3UnormSrgb, new BcPixelFormat(ValueType.UnormSrgb, AlphaType.Straight, 3)},
            {DxgiFormat.Bc4Typeless, new BcPixelFormat(ValueType.Typeless, AlphaType.Straight, 4)},
            {DxgiFormat.Bc4Unorm, new BcPixelFormat(ValueType.Unorm, AlphaType.Straight, 4)},
            {DxgiFormat.Bc4Snorm, new BcPixelFormat(ValueType.Snorm, AlphaType.Straight, 4)},
            {DxgiFormat.Bc5Typeless, new BcPixelFormat(ValueType.Typeless, AlphaType.Straight, 5)},
            {DxgiFormat.Bc5Unorm, new BcPixelFormat(ValueType.Unorm, AlphaType.Straight, 5)},
            {DxgiFormat.Bc5Snorm, new BcPixelFormat(ValueType.Snorm, AlphaType.Straight, 5)},
            {DxgiFormat.B5G6R5Unorm, RgbaxPixelFormat.CreateBgr(ValueType.Unorm, 5, 6, 5)},
            {DxgiFormat.B5G5R5A1Unorm, RgbaxPixelFormat.CreateBgra(ValueType.Unorm, 5, 5, 5, 1)},
            {DxgiFormat.B8G8R8A8Unorm, RgbaxPixelFormat.CreateBgra(ValueType.Unorm, 8, 8, 8, 8)},
            {DxgiFormat.B8G8R8X8Unorm, RgbaxPixelFormat.CreateBgr(ValueType.Unorm, 8, 8, 8, 8)},
            {DxgiFormat.B8G8R8A8Typeless, RgbaxPixelFormat.CreateBgra(ValueType.Typeless, 8, 8, 8, 8)},
            {DxgiFormat.B8G8R8A8UnormSrgb, RgbaxPixelFormat.CreateBgra(ValueType.UnormSrgb, 8, 8, 8, 8)},
            {DxgiFormat.B8G8R8X8Typeless, RgbaxPixelFormat.CreateBgr(ValueType.Typeless, 8, 8, 8, 8)},
            {DxgiFormat.B8G8R8X8UnormSrgb, RgbaxPixelFormat.CreateBgr(ValueType.UnormSrgb, 8, 8, 8, 8)},
            {DxgiFormat.Bc6HTypeless, new BcPixelFormat(ValueType.Typeless, AlphaType.Straight, 6)},
            {DxgiFormat.Bc6HUf16, new BcPixelFormat(ValueType.Uf16, AlphaType.Straight, 6)},
            {DxgiFormat.Bc6HSf16, new BcPixelFormat(ValueType.Sf16, AlphaType.Straight, 6)},
            {DxgiFormat.Bc7Typeless, new BcPixelFormat(ValueType.Typeless, AlphaType.Straight, 7)},
            {DxgiFormat.Bc7Unorm, new BcPixelFormat(ValueType.Unorm, AlphaType.Straight, 7)},
            {DxgiFormat.Bc7UnormSrgb, new BcPixelFormat(ValueType.UnormSrgb, AlphaType.Straight, 7)},
            {DxgiFormat.B4G4R4A4Unorm, RgbaxPixelFormat.CreateBgra(ValueType.Unorm, 4, 4, 4, 4)},
        };
    }

    public static IPixelFormat GetPixelFormat(DdsFourCc fourCc) =>
        FourCcToPixelFormat.TryGetValue(fourCc, out var v) ? v : UnknownPixelFormat.Instance;

    public static IPixelFormat GetPixelFormat(DxgiFormat dxgiFormat) =>
        DxgiFormatToPixelFormat.TryGetValue(dxgiFormat, out var v) ? v : UnknownPixelFormat.Instance;
    
    public static DdsFourCc GetFourCc(IPixelFormat pf) => 
        FourCcToPixelFormat.FirstOrDefault(x => Equals(x.Value, pf)).Key;

    public static DxgiFormat GetDxgiFormat(IPixelFormat pf) => 
        DxgiFormatToPixelFormat.FirstOrDefault(x => Equals(x.Value, pf)).Key;
}
