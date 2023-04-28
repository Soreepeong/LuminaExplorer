using System;
using System.Collections.Generic;
using System.Linq;
using LuminaExplorer.Core.Util.DdsStructs.PixelFormats.Channels;
using WicNet;
using ValueType = LuminaExplorer.Core.Util.DdsStructs.PixelFormats.Channels.ValueType;

namespace LuminaExplorer.Core.Util.DdsStructs.PixelFormats;

public static class PixFmtResolver {
    public static readonly IReadOnlyDictionary<DdsFourCc, IPixFmt> FourCcToPixelFormat;

    public static readonly IReadOnlyDictionary<AlphaType, IReadOnlyDictionary<DxgiFormat, IPixFmt>>
        DxgiFormatToPixelFormat;

    public static readonly IReadOnlyDictionary<Guid, IPixFmt> WicToPixelFormat;

    // https://learn.microsoft.com/en-us/windows/win32/direct3d10/d3d10-graphics-programming-guide-resources-data-conversion
    static PixFmtResolver() {
        FourCcToPixelFormat = new Dictionary<DdsFourCc, IPixFmt> {
            {DdsFourCc.Dxt1, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 1)},
            {DdsFourCc.Dxt2, new BcPixFmt(ValueType.Unorm, AlphaType.Premultiplied, 2)},
            {DdsFourCc.Dxt3, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 2)},
            {DdsFourCc.Dxt4, new BcPixFmt(ValueType.Unorm, AlphaType.Premultiplied, 3)},
            {DdsFourCc.Dxt5, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 3)},
            {DdsFourCc.Bc4, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 4)},
            {DdsFourCc.Bc4U, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 4)},
            {DdsFourCc.Bc4S, new BcPixFmt(ValueType.Snorm, AlphaType.Straight, 4)},
            {DdsFourCc.Bc5, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 5)},
            {DdsFourCc.Bc5U, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 5)},
            {DdsFourCc.Bc5S, new BcPixFmt(ValueType.Snorm, AlphaType.Straight, 5)},
        };
        DxgiFormatToPixelFormat =
            new Dictionary<AlphaType, IReadOnlyDictionary<DxgiFormat, IPixFmt>> {
                {
                    AlphaType.None, new Dictionary<DxgiFormat, IPixFmt> {
                        {
                            DxgiFormat.R32G32B32Typeless,
                            RgbaPixFmt.NewRgb(32, 32, 32, 0, 0, ValueType.Typeless, AlphaType.None)
                        }, {
                            DxgiFormat.R32G32B32Float,
                            RgbaPixFmt.NewRgb(32, 32, 32, 0, 0, ValueType.Float, AlphaType.None)
                        },
                        {DxgiFormat.R32G32B32Uint, RgbaPixFmt.NewRgb(32, 32, 32, 0, 0, ValueType.Uint, AlphaType.None)},
                        {DxgiFormat.R32G32B32Sint, RgbaPixFmt.NewRgb(32, 32, 32, 0, 0, ValueType.Sint, AlphaType.None)},
                        {DxgiFormat.R32G32Float, RgbaPixFmt.NewRg(32, 32, 0, 0, ValueType.Float, AlphaType.None)},
                        {DxgiFormat.R32G32Uint, RgbaPixFmt.NewRg(32, 32, 0, 0, ValueType.Uint, AlphaType.None)},
                        {DxgiFormat.R32G32Sint, RgbaPixFmt.NewRg(32, 32, 0, 0, ValueType.Sint, AlphaType.None)},
                        {DxgiFormat.R16G16Typeless, RgbaPixFmt.NewRg(16, 16, 0, 0, ValueType.Typeless, AlphaType.None)},
                        {DxgiFormat.R16G16Float, RgbaPixFmt.NewRg(16, 16, 0, 0, ValueType.Float, AlphaType.None)},
                        {DxgiFormat.R16G16Unorm, RgbaPixFmt.NewRg(16, 16, 0, 0, ValueType.Unorm, AlphaType.None)},
                        {DxgiFormat.R16G16Uint, RgbaPixFmt.NewRg(16, 16, 0, 0, ValueType.Uint, AlphaType.None)},
                        {DxgiFormat.R16G16Snorm, RgbaPixFmt.NewRg(16, 16, 0, 0, ValueType.Snorm, AlphaType.None)},
                        {DxgiFormat.R16G16Sint, RgbaPixFmt.NewRg(16, 16, 0, 0, ValueType.Sint, AlphaType.None)},
                        {DxgiFormat.R32Typeless, RgbaPixFmt.NewR(32, 0, 0, ValueType.Typeless, AlphaType.None)},
                        {DxgiFormat.R32Float, RgbaPixFmt.NewR(32, 0, 0, ValueType.Float, AlphaType.None)},
                        {DxgiFormat.R32Uint, RgbaPixFmt.NewR(32, 0, 0, ValueType.Uint, AlphaType.None)},
                        {DxgiFormat.R32Sint, RgbaPixFmt.NewR(32, 0, 0, ValueType.Sint, AlphaType.None)},
                        {DxgiFormat.R24G8Typeless, RgbaPixFmt.NewRg(24, 8, 0, 0, ValueType.Typeless, AlphaType.None)},
                        {DxgiFormat.R8G8Typeless, RgbaPixFmt.NewRg(8, 8, 0, 0, ValueType.Typeless, AlphaType.None)},
                        {DxgiFormat.R8G8Unorm, RgbaPixFmt.NewRg(8, 8, 0, 0, ValueType.Unorm, AlphaType.None)},
                        {DxgiFormat.R8G8Uint, RgbaPixFmt.NewRg(8, 8, 0, 0, ValueType.Uint, AlphaType.None)},
                        {DxgiFormat.R8G8Snorm, RgbaPixFmt.NewRg(8, 8, 0, 0, ValueType.Snorm, AlphaType.None)},
                        {DxgiFormat.R8G8Sint, RgbaPixFmt.NewRg(8, 8, 0, 0, ValueType.Sint, AlphaType.None)},
                        {DxgiFormat.R16Typeless, RgbaPixFmt.NewR(16, 0, 0, ValueType.Typeless, AlphaType.None)},
                        {DxgiFormat.R16Float, RgbaPixFmt.NewR(16, 0, 0, ValueType.Float, AlphaType.None)},
                        {DxgiFormat.R16Unorm, RgbaPixFmt.NewR(16, 0, 0, ValueType.Unorm, AlphaType.None)},
                        {DxgiFormat.R16Uint, RgbaPixFmt.NewR(16, 0, 0, ValueType.Uint, AlphaType.None)},
                        {DxgiFormat.R16Snorm, RgbaPixFmt.NewR(16, 0, 0, ValueType.Snorm, AlphaType.None)},
                        {DxgiFormat.R16Sint, RgbaPixFmt.NewR(16, 0, 0, ValueType.Sint, AlphaType.None)},
                        {DxgiFormat.R8Typeless, RgbaPixFmt.NewR(8, 0, 0, ValueType.Typeless, AlphaType.None)},
                        {DxgiFormat.R8Unorm, RgbaPixFmt.NewR(8, 0, 0, ValueType.Float, AlphaType.None)},
                        {DxgiFormat.R8Uint, RgbaPixFmt.NewR(8, 0, 0, ValueType.Unorm, AlphaType.None)},
                        {DxgiFormat.R8Snorm, RgbaPixFmt.NewR(8, 0, 0, ValueType.Uint, AlphaType.None)},
                        {DxgiFormat.R8Sint, RgbaPixFmt.NewR(8, 0, 0, ValueType.Sint, AlphaType.None)},
                        {DxgiFormat.B5G6R5Unorm, RgbaPixFmt.NewBgr(5, 6, 5, 0, 0, ValueType.Unorm, AlphaType.None)},
                        {DxgiFormat.Bc1Typeless, new BcPixFmt(ValueType.Typeless, AlphaType.None, 1)},
                        {DxgiFormat.Bc1Unorm, new BcPixFmt(ValueType.Unorm, AlphaType.None, 1)},
                        {DxgiFormat.Bc1UnormSrgb, new BcPixFmt(ValueType.UnormSrgb, AlphaType.None, 1)},
                        {DxgiFormat.Bc2Typeless, new BcPixFmt(ValueType.Typeless, AlphaType.None, 2)},
                        {DxgiFormat.Bc2Unorm, new BcPixFmt(ValueType.Unorm, AlphaType.None, 2)},
                        {DxgiFormat.Bc2UnormSrgb, new BcPixFmt(ValueType.UnormSrgb, AlphaType.None, 2)},
                        {DxgiFormat.Bc3Typeless, new BcPixFmt(ValueType.Typeless, AlphaType.None, 3)},
                        {DxgiFormat.Bc3Unorm, new BcPixFmt(ValueType.Unorm, AlphaType.None, 3)},
                        {DxgiFormat.Bc3UnormSrgb, new BcPixFmt(ValueType.UnormSrgb, AlphaType.None, 3)},
                        {DxgiFormat.Bc4Typeless, new BcPixFmt(ValueType.Typeless, AlphaType.None, 4)},
                        {DxgiFormat.Bc4Unorm, new BcPixFmt(ValueType.Unorm, AlphaType.None, 4)},
                        {DxgiFormat.Bc4Snorm, new BcPixFmt(ValueType.Snorm, AlphaType.None, 4)},
                        {DxgiFormat.Bc5Typeless, new BcPixFmt(ValueType.Typeless, AlphaType.None, 5)},
                        {DxgiFormat.Bc5Unorm, new BcPixFmt(ValueType.Unorm, AlphaType.None, 5)},
                        {DxgiFormat.Bc5Snorm, new BcPixFmt(ValueType.Snorm, AlphaType.None, 5)},
                        {DxgiFormat.Bc6HTypeless, new BcPixFmt(ValueType.Typeless, AlphaType.None, 6)},
                        {DxgiFormat.Bc6HUf16, new BcPixFmt(ValueType.Uf16, AlphaType.None, 6)},
                        {DxgiFormat.Bc6HSf16, new BcPixFmt(ValueType.Sf16, AlphaType.None, 6)},
                        {DxgiFormat.Bc7Typeless, new BcPixFmt(ValueType.Typeless, AlphaType.None, 7)},
                        {DxgiFormat.Bc7Unorm, new BcPixFmt(ValueType.Unorm, AlphaType.None, 7)},
                        {DxgiFormat.Bc7UnormSrgb, new BcPixFmt(ValueType.UnormSrgb, AlphaType.None, 7)},
                    }
                }, {
                    AlphaType.Straight, new Dictionary<DxgiFormat, IPixFmt> {
                        {DxgiFormat.R32G32B32A32Typeless, RgbaPixFmt.NewRgba(32, 32, 32, 32, 0, 0, ValueType.Typeless)},
                        {DxgiFormat.R32G32B32A32Float, RgbaPixFmt.NewRgba(32, 32, 32, 32, 0, 0, ValueType.Float)},
                        {DxgiFormat.R32G32B32A32Uint, RgbaPixFmt.NewRgba(32, 32, 32, 32, 0, 0, ValueType.Uint)},
                        {DxgiFormat.R32G32B32A32Sint, RgbaPixFmt.NewRgba(32, 32, 32, 32, 0, 0, ValueType.Sint)},
                        {DxgiFormat.R16G16B16A16Typeless, RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Typeless)},
                        {DxgiFormat.R16G16B16A16Float, RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Float)},
                        {DxgiFormat.R16G16B16A16Unorm, RgbaPixFmt.NewRgba(16, 16, 16, 16)},
                        {DxgiFormat.R16G16B16A16Uint, RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Uint)},
                        {DxgiFormat.R16G16B16A16Snorm, RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Snorm)},
                        {DxgiFormat.R16G16B16A16Sint, RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Sint)},
                        {DxgiFormat.R10G10B10A2Typeless, RgbaPixFmt.NewRgba(10, 10, 10, 2, 0, 0, ValueType.Typeless)},
                        {DxgiFormat.R10G10B10A2Unorm, RgbaPixFmt.NewRgba(10, 10, 10, 2)},
                        {DxgiFormat.R10G10B10A2Uint, RgbaPixFmt.NewRgba(10, 10, 10, 2, 0, 0, ValueType.Uint)},
                        {DxgiFormat.R8G8B8A8Typeless, RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.Typeless)},
                        {DxgiFormat.R8G8B8A8Unorm, RgbaPixFmt.NewRgba(8, 8, 8, 8)},
                        {DxgiFormat.R8G8B8A8UnormSrgb, RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.UnormSrgb)},
                        {DxgiFormat.R8G8B8A8Uint, RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.Uint)},
                        {DxgiFormat.R8G8B8A8Snorm, RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.Snorm)},
                        {DxgiFormat.R8G8B8A8Sint, RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.Sint)},
                        {DxgiFormat.A8Unorm, RgbaPixFmt.NewA(8)},
                        {DxgiFormat.B5G5R5A1Unorm, RgbaPixFmt.NewBgra(5, 5, 5, 1)},
                        {DxgiFormat.B8G8R8A8Unorm, RgbaPixFmt.NewBgra(8, 8, 8, 8)},
                        {DxgiFormat.B8G8R8A8Typeless, RgbaPixFmt.NewBgra(8, 8, 8, 8, 0, 0, ValueType.Typeless)},
                        {DxgiFormat.B8G8R8A8UnormSrgb, RgbaPixFmt.NewBgra(8, 8, 8, 8, 0, 0, ValueType.UnormSrgb)},
                        {DxgiFormat.B4G4R4A4Unorm, RgbaPixFmt.NewBgra(4, 4, 4, 4)},
                        {DxgiFormat.Bc1Typeless, new BcPixFmt(ValueType.Typeless, AlphaType.Straight, 1)},
                        {DxgiFormat.Bc1Unorm, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 1)},
                        {DxgiFormat.Bc1UnormSrgb, new BcPixFmt(ValueType.UnormSrgb, AlphaType.Straight, 1)},
                        {DxgiFormat.Bc2Typeless, new BcPixFmt(ValueType.Typeless, AlphaType.Straight, 2)},
                        {DxgiFormat.Bc2Unorm, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 2)},
                        {DxgiFormat.Bc2UnormSrgb, new BcPixFmt(ValueType.UnormSrgb, AlphaType.Straight, 2)},
                        {DxgiFormat.Bc3Typeless, new BcPixFmt(ValueType.Typeless, AlphaType.Straight, 3)},
                        {DxgiFormat.Bc3Unorm, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 3)},
                        {DxgiFormat.Bc3UnormSrgb, new BcPixFmt(ValueType.UnormSrgb, AlphaType.Straight, 3)},
                        {DxgiFormat.Bc4Typeless, new BcPixFmt(ValueType.Typeless, AlphaType.Straight, 4)},
                        {DxgiFormat.Bc4Unorm, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 4)},
                        {DxgiFormat.Bc4Snorm, new BcPixFmt(ValueType.Snorm, AlphaType.Straight, 4)},
                        {DxgiFormat.Bc5Typeless, new BcPixFmt(ValueType.Typeless, AlphaType.Straight, 5)},
                        {DxgiFormat.Bc5Unorm, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 5)},
                        {DxgiFormat.Bc5Snorm, new BcPixFmt(ValueType.Snorm, AlphaType.Straight, 5)},
                        {DxgiFormat.Bc6HTypeless, new BcPixFmt(ValueType.Typeless, AlphaType.Straight, 6)},
                        {DxgiFormat.Bc6HUf16, new BcPixFmt(ValueType.Uf16, AlphaType.Straight, 6)},
                        {DxgiFormat.Bc6HSf16, new BcPixFmt(ValueType.Sf16, AlphaType.Straight, 6)},
                        {DxgiFormat.Bc7Typeless, new BcPixFmt(ValueType.Typeless, AlphaType.Straight, 7)},
                        {DxgiFormat.Bc7Unorm, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 7)},
                        {DxgiFormat.Bc7UnormSrgb, new BcPixFmt(ValueType.UnormSrgb, AlphaType.Straight, 7)},
                    }
                }, {
                    AlphaType.Premultiplied, new Dictionary<DxgiFormat, IPixFmt> {
                        {
                            DxgiFormat.R32G32B32A32Typeless,
                            RgbaPixFmt.NewRgba(32, 32, 32, 32, 0, 0, ValueType.Typeless, AlphaType.Premultiplied)
                        }, {
                            DxgiFormat.R32G32B32A32Float,
                            RgbaPixFmt.NewRgba(32, 32, 32, 32, 0, 0, ValueType.Float, AlphaType.Premultiplied)
                        }, {
                            DxgiFormat.R32G32B32A32Uint,
                            RgbaPixFmt.NewRgba(32, 32, 32, 32, 0, 0, ValueType.Uint, AlphaType.Premultiplied)
                        }, {
                            DxgiFormat.R32G32B32A32Sint,
                            RgbaPixFmt.NewRgba(32, 32, 32, 32, 0, 0, ValueType.Sint, AlphaType.Premultiplied)
                        }, {
                            DxgiFormat.R16G16B16A16Typeless,
                            RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Typeless, AlphaType.Premultiplied)
                        }, {
                            DxgiFormat.R16G16B16A16Float,
                            RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Float, AlphaType.Premultiplied)
                        }, {
                            DxgiFormat.R16G16B16A16Unorm,
                            RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Unorm, AlphaType.Premultiplied)
                        }, {
                            DxgiFormat.R16G16B16A16Uint,
                            RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Uint, AlphaType.Premultiplied)
                        }, {
                            DxgiFormat.R16G16B16A16Snorm,
                            RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Snorm, AlphaType.Premultiplied)
                        }, {
                            DxgiFormat.R16G16B16A16Sint,
                            RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Sint, AlphaType.Premultiplied)
                        }, {
                            DxgiFormat.R10G10B10A2Typeless,
                            RgbaPixFmt.NewRgba(10, 10, 10, 2, 0, 0, ValueType.Typeless, AlphaType.Premultiplied)
                        }, {
                            DxgiFormat.R10G10B10A2Unorm,
                            RgbaPixFmt.NewRgba(10, 10, 10, 2, 0, 0, ValueType.Unorm, AlphaType.Premultiplied)
                        }, {
                            DxgiFormat.R10G10B10A2Uint,
                            RgbaPixFmt.NewRgba(10, 10, 10, 2, 0, 0, ValueType.Uint, AlphaType.Premultiplied)
                        }, {
                            DxgiFormat.R8G8B8A8Typeless,
                            RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.Typeless, AlphaType.Premultiplied)
                        }, {
                            DxgiFormat.R8G8B8A8Unorm,
                            RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.Unorm, AlphaType.Premultiplied)
                        }, {
                            DxgiFormat.R8G8B8A8UnormSrgb,
                            RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.UnormSrgb, AlphaType.Premultiplied)
                        }, {
                            DxgiFormat.R8G8B8A8Uint,
                            RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.Uint, AlphaType.Premultiplied)
                        }, {
                            DxgiFormat.R8G8B8A8Snorm,
                            RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.Snorm, AlphaType.Premultiplied)
                        }, {
                            DxgiFormat.R8G8B8A8Sint,
                            RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.Sint, AlphaType.Premultiplied)
                        },
                        {DxgiFormat.A8Unorm, RgbaPixFmt.NewA(8, 0, 0, ValueType.Unorm, AlphaType.Premultiplied)}, {
                            DxgiFormat.B5G5R5A1Unorm,
                            RgbaPixFmt.NewBgra(5, 5, 5, 1, 0, 0, ValueType.Unorm, AlphaType.Premultiplied)
                        }, {
                            DxgiFormat.B8G8R8A8Unorm,
                            RgbaPixFmt.NewBgra(8, 8, 8, 8, 0, 0, ValueType.Unorm, AlphaType.Premultiplied)
                        }, {
                            DxgiFormat.B8G8R8A8Typeless,
                            RgbaPixFmt.NewBgra(8, 8, 8, 8, 0, 0, ValueType.Typeless, AlphaType.Premultiplied)
                        }, {
                            DxgiFormat.B8G8R8A8UnormSrgb,
                            RgbaPixFmt.NewBgra(8, 8, 8, 8, 0, 0, ValueType.UnormSrgb, AlphaType.Premultiplied)
                        }, {
                            DxgiFormat.B4G4R4A4Unorm,
                            RgbaPixFmt.NewBgra(4, 4, 4, 4, 0, 0, ValueType.Unorm, AlphaType.Premultiplied)
                        },
                        {DxgiFormat.Bc1Typeless, new BcPixFmt(ValueType.Typeless, AlphaType.Premultiplied, 1)},
                        {DxgiFormat.Bc1Unorm, new BcPixFmt(ValueType.Unorm, AlphaType.Premultiplied, 1)},
                        {DxgiFormat.Bc1UnormSrgb, new BcPixFmt(ValueType.UnormSrgb, AlphaType.Premultiplied, 1)},
                        {DxgiFormat.Bc2Typeless, new BcPixFmt(ValueType.Typeless, AlphaType.Premultiplied, 2)},
                        {DxgiFormat.Bc2Unorm, new BcPixFmt(ValueType.Unorm, AlphaType.Premultiplied, 2)},
                        {DxgiFormat.Bc2UnormSrgb, new BcPixFmt(ValueType.UnormSrgb, AlphaType.Premultiplied, 2)},
                        {DxgiFormat.Bc3Typeless, new BcPixFmt(ValueType.Typeless, AlphaType.Premultiplied, 3)},
                        {DxgiFormat.Bc3Unorm, new BcPixFmt(ValueType.Unorm, AlphaType.Premultiplied, 3)},
                        {DxgiFormat.Bc3UnormSrgb, new BcPixFmt(ValueType.UnormSrgb, AlphaType.Premultiplied, 3)},
                        {DxgiFormat.Bc4Typeless, new BcPixFmt(ValueType.Typeless, AlphaType.Premultiplied, 4)},
                        {DxgiFormat.Bc4Unorm, new BcPixFmt(ValueType.Unorm, AlphaType.Premultiplied, 4)},
                        {DxgiFormat.Bc4Snorm, new BcPixFmt(ValueType.Snorm, AlphaType.Premultiplied, 4)},
                        {DxgiFormat.Bc5Typeless, new BcPixFmt(ValueType.Typeless, AlphaType.Premultiplied, 5)},
                        {DxgiFormat.Bc5Unorm, new BcPixFmt(ValueType.Unorm, AlphaType.Premultiplied, 5)},
                        {DxgiFormat.Bc5Snorm, new BcPixFmt(ValueType.Snorm, AlphaType.Premultiplied, 5)},
                        {DxgiFormat.Bc6HTypeless, new BcPixFmt(ValueType.Typeless, AlphaType.Premultiplied, 6)},
                        {DxgiFormat.Bc6HUf16, new BcPixFmt(ValueType.Uf16, AlphaType.Premultiplied, 6)},
                        {DxgiFormat.Bc6HSf16, new BcPixFmt(ValueType.Sf16, AlphaType.Premultiplied, 6)},
                        {DxgiFormat.Bc7Typeless, new BcPixFmt(ValueType.Typeless, AlphaType.Premultiplied, 7)},
                        {DxgiFormat.Bc7Unorm, new BcPixFmt(ValueType.Unorm, AlphaType.Premultiplied, 7)},
                        {DxgiFormat.Bc7UnormSrgb, new BcPixFmt(ValueType.UnormSrgb, AlphaType.Premultiplied, 7)},
                    }
                }, {
                    AlphaType.Custom, new Dictionary<DxgiFormat, IPixFmt> {
                        {DxgiFormat.B8G8R8X8Unorm, RgbaPixFmt.NewBgr(8, 8, 8, 8, 0, ValueType.Unorm, AlphaType.Custom)}, {
                            DxgiFormat.B8G8R8X8Typeless,
                            RgbaPixFmt.NewBgr(8, 8, 8, 8, 0, ValueType.Typeless, AlphaType.Custom)
                        }, {
                            DxgiFormat.B8G8R8X8UnormSrgb,
                            RgbaPixFmt.NewBgr(8, 8, 8, 8, 0, ValueType.UnormSrgb, AlphaType.Custom)
                        },
                        {DxgiFormat.Bc1Typeless, new BcPixFmt(ValueType.Typeless, AlphaType.Custom, 1)},
                        {DxgiFormat.Bc1Unorm, new BcPixFmt(ValueType.Unorm, AlphaType.Custom, 1)},
                        {DxgiFormat.Bc1UnormSrgb, new BcPixFmt(ValueType.UnormSrgb, AlphaType.Custom, 1)},
                        {DxgiFormat.Bc2Typeless, new BcPixFmt(ValueType.Typeless, AlphaType.Custom, 2)},
                        {DxgiFormat.Bc2Unorm, new BcPixFmt(ValueType.Unorm, AlphaType.Custom, 2)},
                        {DxgiFormat.Bc2UnormSrgb, new BcPixFmt(ValueType.UnormSrgb, AlphaType.Custom, 2)},
                        {DxgiFormat.Bc3Typeless, new BcPixFmt(ValueType.Typeless, AlphaType.Custom, 3)},
                        {DxgiFormat.Bc3Unorm, new BcPixFmt(ValueType.Unorm, AlphaType.Custom, 3)},
                        {DxgiFormat.Bc3UnormSrgb, new BcPixFmt(ValueType.UnormSrgb, AlphaType.Custom, 3)},
                        {DxgiFormat.Bc4Typeless, new BcPixFmt(ValueType.Typeless, AlphaType.Custom, 4)},
                        {DxgiFormat.Bc4Unorm, new BcPixFmt(ValueType.Unorm, AlphaType.Custom, 4)},
                        {DxgiFormat.Bc4Snorm, new BcPixFmt(ValueType.Snorm, AlphaType.Custom, 4)},
                        {DxgiFormat.Bc5Typeless, new BcPixFmt(ValueType.Typeless, AlphaType.Custom, 5)},
                        {DxgiFormat.Bc5Unorm, new BcPixFmt(ValueType.Unorm, AlphaType.Custom, 5)},
                        {DxgiFormat.Bc5Snorm, new BcPixFmt(ValueType.Snorm, AlphaType.Custom, 5)},
                        {DxgiFormat.Bc6HTypeless, new BcPixFmt(ValueType.Typeless, AlphaType.Custom, 6)},
                        {DxgiFormat.Bc6HUf16, new BcPixFmt(ValueType.Uf16, AlphaType.Custom, 6)},
                        {DxgiFormat.Bc6HSf16, new BcPixFmt(ValueType.Sf16, AlphaType.Custom, 6)},
                        {DxgiFormat.Bc7Typeless, new BcPixFmt(ValueType.Typeless, AlphaType.Custom, 7)},
                        {DxgiFormat.Bc7Unorm, new BcPixFmt(ValueType.Unorm, AlphaType.Custom, 7)},
                        {DxgiFormat.Bc7UnormSrgb, new BcPixFmt(ValueType.UnormSrgb, AlphaType.Custom, 7)},
                    }
                }
            };

        // https://learn.microsoft.com/en-us/windows/win32/wic/-wic-codec-native-pixel-formats#packed-bit-pixel-formats
        // Note: below list might got byte orders wrong (Bgr/Rgb)
        WicToPixelFormat = new Dictionary<Guid, IPixFmt> {
            // Packed Bit Pixel Formats
            {
                WicPixelFormat.GUID_WICPixelFormat16bppBGR555,
                RgbaPixFmt.NewBgr(5, 5, 5, 1, 0, ValueType.Unorm, AlphaType.None)
            }, {
                WicPixelFormat.GUID_WICPixelFormat16bppBGR565,
                RgbaPixFmt.NewBgr(5, 6, 5, 0, 0, ValueType.Unorm, AlphaType.None)
            }, {
                WicPixelFormat.GUID_WICPixelFormat16bppBGRA5551,
                RgbaPixFmt.NewBgra(5, 5, 5, 1, 0, 0, ValueType.Unorm, AlphaType.None)
            }, {
                WicPixelFormat.GUID_WICPixelFormat32bppBGR101010,
                RgbaPixFmt.NewBgr(10, 10, 10, 2, 0, ValueType.Unorm, AlphaType.None)
            }, {
                WicPixelFormat.GUID_WICPixelFormat32bppRGBA1010102,
                RgbaPixFmt.NewRgba(10, 10, 10, 2, 0, 0, ValueType.Unorm, AlphaType.None)
            }, {
                WicPixelFormat.GUID_WICPixelFormat32bppR10G10B10A2,
                RgbaPixFmt.NewBgra(10, 10, 10, 2, 0, 0, ValueType.Unorm, AlphaType.None)
            },

            // Grayscale Pixel Formats
            {WicPixelFormat.GUID_WICPixelFormatBlackWhite, new LumiPixFmt(AlphaType.None, new(ValueType.Unorm, 0, 1))},
            {WicPixelFormat.GUID_WICPixelFormat2bppGray, new LumiPixFmt(AlphaType.None, new(ValueType.Unorm, 0, 2))},
            {WicPixelFormat.GUID_WICPixelFormat4bppGray, new LumiPixFmt(AlphaType.None, new(ValueType.Unorm, 0, 4))},
            {WicPixelFormat.GUID_WICPixelFormat8bppGray, new LumiPixFmt(AlphaType.None, new(ValueType.Unorm, 0, 8))},
            {WicPixelFormat.GUID_WICPixelFormat16bppGray, new LumiPixFmt(AlphaType.None, new(ValueType.Unorm, 0, 16))}, {
                WicPixelFormat.GUID_WICPixelFormat16bppGrayHalf,
                new LumiPixFmt(AlphaType.None, new(ValueType.Half, 0, 16))
            }, {
                WicPixelFormat.GUID_WICPixelFormat32bppGrayFloat,
                new LumiPixFmt(AlphaType.None, new(ValueType.Float, 0, 32))
            },

            // RGB/BGR Pixel formats
            {
                WicPixelFormat.GUID_WICPixelFormat24bppRGB,
                RgbaPixFmt.NewRgb(8, 8, 8, 0, 0, ValueType.Unorm, AlphaType.None)
            }, {
                WicPixelFormat.GUID_WICPixelFormat24bppBGR,
                RgbaPixFmt.NewBgr(8, 8, 8, 0, 0, ValueType.Unorm, AlphaType.None)
            }, {
                WicPixelFormat.GUID_WICPixelFormat32bppBGR,
                RgbaPixFmt.NewBgr(8, 8, 8, 8, 0, ValueType.Unorm, AlphaType.None)
            }, {
                WicPixelFormat.GUID_WICPixelFormat32bppRGBA,
                RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.Unorm, AlphaType.Straight)
            }, {
                WicPixelFormat.GUID_WICPixelFormat32bppBGRA,
                RgbaPixFmt.NewBgra(8, 8, 8, 8, 0, 0, ValueType.Unorm, AlphaType.Straight)
            }, {
                WicPixelFormat.GUID_WICPixelFormat32bppPRGBA,
                RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.Unorm, AlphaType.Premultiplied)
            }, {
                WicPixelFormat.GUID_WICPixelFormat32bppPBGRA,
                RgbaPixFmt.NewBgra(8, 8, 8, 8, 0, 0, ValueType.Unorm, AlphaType.Premultiplied)
            }, {
                WicPixelFormat.GUID_WICPixelFormat48bppRGB,
                RgbaPixFmt.NewRgba(16, 16, 16, 0, 0, 0, ValueType.Unorm, AlphaType.None)
            }, {
                WicPixelFormat.GUID_WICPixelFormat48bppBGR,
                RgbaPixFmt.NewBgra(16, 16, 16, 0, 0, 0, ValueType.Unorm, AlphaType.None)
            }, {
                WicPixelFormat.GUID_WICPixelFormat48bppRGBHalf,
                RgbaPixFmt.NewRgba(16, 16, 16, 0, 0, 0, ValueType.Half, AlphaType.None)
            }, {
                WicPixelFormat.GUID_WICPixelFormat64bppRGBA,
                RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Unorm, AlphaType.Straight)
            }, {
                WicPixelFormat.GUID_WICPixelFormat64bppBGRA,
                RgbaPixFmt.NewBgra(16, 16, 16, 16, 0, 0, ValueType.Unorm, AlphaType.Straight)
            }, {
                WicPixelFormat.GUID_WICPixelFormat64bppPRGBA,
                RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Unorm, AlphaType.Premultiplied)
            }, {
                WicPixelFormat.GUID_WICPixelFormat64bppPBGRA,
                RgbaPixFmt.NewBgra(16, 16, 16, 16, 0, 0, ValueType.Unorm, AlphaType.Premultiplied)
            }, {
                WicPixelFormat.GUID_WICPixelFormat64bppRGBHalf,
                RgbaPixFmt.NewRgb(16, 16, 16, 16, 0, ValueType.Half, AlphaType.None)
            }, {
                WicPixelFormat.GUID_WICPixelFormat64bppRGBAHalf,
                RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Half, AlphaType.None)
            }, {
                WicPixelFormat.GUID_WICPixelFormat128bppRGBFloat,
                RgbaPixFmt.NewRgb(32, 32, 32, 32, 0, ValueType.Float, AlphaType.None)
            }, {
                WicPixelFormat.GUID_WICPixelFormat128bppRGBAFloat,
                RgbaPixFmt.NewRgba(32, 32, 32, 32, 0, 0, ValueType.Float, AlphaType.Straight)
            }, {
                WicPixelFormat.GUID_WICPixelFormat128bppPRGBAFloat,
                RgbaPixFmt.NewRgba(32, 32, 32, 32, 0, 0, ValueType.Float, AlphaType.Premultiplied)
            },

            // RGB/BGR Pixel formats (Windows 8 & Platform Update for Windows 7)
            {
                WicPixelFormat.GUID_WICPixelFormat32bppRGB,
                RgbaPixFmt.NewRgb(8, 8, 8, 8, 0, ValueType.Unorm, AlphaType.None)
            }, {
                WicPixelFormat.GUID_WICPixelFormat64bppRGB,
                RgbaPixFmt.NewRgb(16, 16, 16, 16, 0, ValueType.Unorm, AlphaType.None)
            }, {
                WicPixelFormat.GUID_WICPixelFormat96bppRGBFloat,
                RgbaPixFmt.NewRgb(32, 32, 32, 0, 0, ValueType.Float, AlphaType.None)
            }, {
                WicPixelFormat.GUID_WICPixelFormat64bppPRGBAHalf,
                RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Half, AlphaType.Premultiplied)
            },
        };
    }

    public static IPixFmt GetPixelFormat(DdsFourCc fourCc) =>
        FourCcToPixelFormat.TryGetValue(fourCc, out var v) ? v : UnknownPixFmt.Instance;

    public static IPixFmt GetPixelFormat(AlphaType alphaType, DxgiFormat dxgiFormat) =>
        DxgiFormatToPixelFormat.TryGetValue(alphaType, out var d1)
            ? d1.TryGetValue(dxgiFormat, out var pf)
                ? pf
                : UnknownPixFmt.Instance
            : UnknownPixFmt.Instance;

    public static IPixFmt GetPixelFormat(Guid pixelFormatGuid) =>
        WicToPixelFormat.TryGetValue(pixelFormatGuid, out var v) ? v : UnknownPixFmt.Instance;

    public static DdsFourCc GetFourCc(IPixFmt pf) =>
        FourCcToPixelFormat.FirstOrDefault(x => Equals(x.Value, pf)).Key;

    public static DxgiFormat GetDxgiFormat(IPixFmt pf) =>
        DxgiFormatToPixelFormat.TryGetValue(pf.Alpha, out var d1)
            ? d1.FirstOrDefault(x => Equals(x.Value, pf)).Key
            : DxgiFormat.Unknown;

    public static Guid GetWicPixelFormat(IPixFmt pf) {
        var r = WicToPixelFormat.FirstOrDefault(x => Equals(x.Value, pf)).Key;
        return r == Guid.Empty ? WicPixelFormat.GUID_WICPixelFormatUndefined : r;
    }
}
