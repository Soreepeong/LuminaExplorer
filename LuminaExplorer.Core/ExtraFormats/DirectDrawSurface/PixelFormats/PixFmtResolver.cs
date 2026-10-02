using System;
using System.Collections.Generic;
using System.Linq;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface.PixelFormats.Channels;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using ValueType = LuminaExplorer.Core.ExtraFormats.DirectDrawSurface.PixelFormats.Channels.ValueType;
using static TerraFX.Interop.DirectX.DXGI_FORMAT;

namespace LuminaExplorer.Core.ExtraFormats.DirectDrawSurface.PixelFormats;

public static class PixFmtResolver {
    public static readonly IReadOnlyDictionary<DdsFourCc, IPixFmt> FourCcToPixelFormat;

    public static readonly IReadOnlyDictionary<AlphaType, IReadOnlyDictionary<DXGI_FORMAT, IPixFmt>>
        DxgiFormatToPixelFormat;

    public static readonly IReadOnlyDictionary<Guid, IPixFmt> WicToPixelFormat;

    // https://learn.microsoft.com/en-us/windows/win32/direct3d10/d3d10-graphics-programming-guide-resources-data-conversion
    static PixFmtResolver()
    {
        FourCcToPixelFormat = new Dictionary<DdsFourCc, IPixFmt> {
            { DdsFourCc.Dxt1, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 1) },
            { DdsFourCc.Dxt2, new BcPixFmt(ValueType.Unorm, AlphaType.Premultiplied, 2) },
            { DdsFourCc.Dxt3, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 2) },
            { DdsFourCc.Dxt4, new BcPixFmt(ValueType.Unorm, AlphaType.Premultiplied, 3) },
            { DdsFourCc.Dxt5, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 3) },
            { DdsFourCc.Bc4, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 4) },
            { DdsFourCc.Bc4U, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 4) },
            { DdsFourCc.Bc4S, new BcPixFmt(ValueType.Snorm, AlphaType.Straight, 4) },
            { DdsFourCc.Bc5, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 5) },
            { DdsFourCc.Bc5U, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 5) },
            { DdsFourCc.Bc5S, new BcPixFmt(ValueType.Snorm, AlphaType.Straight, 5) },
        };
        DxgiFormatToPixelFormat =
            new Dictionary<AlphaType, IReadOnlyDictionary<DXGI_FORMAT, IPixFmt>> {
                {
                    AlphaType.None, new Dictionary<DXGI_FORMAT, IPixFmt> {
                        {
                            DXGI_FORMAT_R32G32B32_TYPELESS,
                            RgbaPixFmt.NewRgb(32, 32, 32, 0, 0, ValueType.Typeless, AlphaType.None)
                        }, {
                            DXGI_FORMAT_R32G32B32_FLOAT,
                            RgbaPixFmt.NewRgb(32, 32, 32, 0, 0, ValueType.Float, AlphaType.None)
                        }, {
                            DXGI_FORMAT_R32G32B32_UINT,
                            RgbaPixFmt.NewRgb(32, 32, 32, 0, 0, ValueType.Uint, AlphaType.None)
                        }, {
                            DXGI_FORMAT_R32G32B32_SINT,
                            RgbaPixFmt.NewRgb(32, 32, 32, 0, 0, ValueType.Sint, AlphaType.None)
                        },
                        { DXGI_FORMAT_R32G32_FLOAT, RgbaPixFmt.NewRg(32, 32, 0, 0, ValueType.Float, AlphaType.None) },
                        { DXGI_FORMAT_R32G32_UINT, RgbaPixFmt.NewRg(32, 32, 0, 0, ValueType.Uint, AlphaType.None) },
                        { DXGI_FORMAT_R32G32_SINT, RgbaPixFmt.NewRg(32, 32, 0, 0, ValueType.Sint, AlphaType.None) }, {
                            DXGI_FORMAT_R16G16_TYPELESS,
                            RgbaPixFmt.NewRg(16, 16, 0, 0, ValueType.Typeless, AlphaType.None)
                        },
                        { DXGI_FORMAT_R16G16_FLOAT, RgbaPixFmt.NewRg(16, 16, 0, 0, ValueType.Float, AlphaType.None) },
                        { DXGI_FORMAT_R16G16_UNORM, RgbaPixFmt.NewRg(16, 16, 0, 0, ValueType.Unorm, AlphaType.None) },
                        { DXGI_FORMAT_R16G16_UINT, RgbaPixFmt.NewRg(16, 16, 0, 0, ValueType.Uint, AlphaType.None) },
                        { DXGI_FORMAT_R16G16_SNORM, RgbaPixFmt.NewRg(16, 16, 0, 0, ValueType.Snorm, AlphaType.None) },
                        { DXGI_FORMAT_R16G16_SINT, RgbaPixFmt.NewRg(16, 16, 0, 0, ValueType.Sint, AlphaType.None) },
                        { DXGI_FORMAT_R32_TYPELESS, RgbaPixFmt.NewR(32, 0, 0, ValueType.Typeless, AlphaType.None) },
                        { DXGI_FORMAT_R32_FLOAT, RgbaPixFmt.NewR(32, 0, 0, ValueType.Float, AlphaType.None) },
                        { DXGI_FORMAT_R32_UINT, RgbaPixFmt.NewR(32, 0, 0, ValueType.Uint, AlphaType.None) },
                        { DXGI_FORMAT_R32_SINT, RgbaPixFmt.NewR(32, 0, 0, ValueType.Sint, AlphaType.None) }, {
                            DXGI_FORMAT_R24G8_TYPELESS,
                            RgbaPixFmt.NewRg(24, 8, 0, 0, ValueType.Typeless, AlphaType.None)
                        },
                        { DXGI_FORMAT_R8G8_TYPELESS, RgbaPixFmt.NewRg(8, 8, 0, 0, ValueType.Typeless, AlphaType.None) },
                        { DXGI_FORMAT_R8G8_UNORM, RgbaPixFmt.NewRg(8, 8, 0, 0, ValueType.Unorm, AlphaType.None) },
                        { DXGI_FORMAT_R8G8_UINT, RgbaPixFmt.NewRg(8, 8, 0, 0, ValueType.Uint, AlphaType.None) },
                        { DXGI_FORMAT_R8G8_SNORM, RgbaPixFmt.NewRg(8, 8, 0, 0, ValueType.Snorm, AlphaType.None) },
                        { DXGI_FORMAT_R8G8_SINT, RgbaPixFmt.NewRg(8, 8, 0, 0, ValueType.Sint, AlphaType.None) },
                        { DXGI_FORMAT_R16_TYPELESS, RgbaPixFmt.NewR(16, 0, 0, ValueType.Typeless, AlphaType.None) },
                        { DXGI_FORMAT_R16_FLOAT, RgbaPixFmt.NewR(16, 0, 0, ValueType.Float, AlphaType.None) },
                        { DXGI_FORMAT_R16_UNORM, RgbaPixFmt.NewR(16, 0, 0, ValueType.Unorm, AlphaType.None) },
                        { DXGI_FORMAT_R16_UINT, RgbaPixFmt.NewR(16, 0, 0, ValueType.Uint, AlphaType.None) },
                        { DXGI_FORMAT_R16_SNORM, RgbaPixFmt.NewR(16, 0, 0, ValueType.Snorm, AlphaType.None) },
                        { DXGI_FORMAT_R16_SINT, RgbaPixFmt.NewR(16, 0, 0, ValueType.Sint, AlphaType.None) },
                        { DXGI_FORMAT_R8_TYPELESS, RgbaPixFmt.NewR(8, 0, 0, ValueType.Typeless, AlphaType.None) },
                        { DXGI_FORMAT_R8_UNORM, RgbaPixFmt.NewR(8, 0, 0, ValueType.Unorm, AlphaType.None) },
                        { DXGI_FORMAT_R8_UINT, RgbaPixFmt.NewR(8, 0, 0, ValueType.Uint, AlphaType.None) },
                        { DXGI_FORMAT_R8_SNORM, RgbaPixFmt.NewR(8, 0, 0, ValueType.Snorm, AlphaType.None) },
                        { DXGI_FORMAT_R8_SINT, RgbaPixFmt.NewR(8, 0, 0, ValueType.Sint, AlphaType.None) },
                        { DXGI_FORMAT_B5G6R5_UNORM, RgbaPixFmt.NewBgr(5, 6, 5, 0, 0, ValueType.Unorm, AlphaType.None) },
                        { DXGI_FORMAT_BC1_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.None, 1) },
                        { DXGI_FORMAT_BC1_UNORM, new BcPixFmt(ValueType.Unorm, AlphaType.None, 1) },
                        { DXGI_FORMAT_BC1_UNORM_SRGB, new BcPixFmt(ValueType.UnormSrgb, AlphaType.None, 1) },
                        { DXGI_FORMAT_BC2_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.None, 2) },
                        { DXGI_FORMAT_BC2_UNORM, new BcPixFmt(ValueType.Unorm, AlphaType.None, 2) },
                        { DXGI_FORMAT_BC2_UNORM_SRGB, new BcPixFmt(ValueType.UnormSrgb, AlphaType.None, 2) },
                        { DXGI_FORMAT_BC3_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.None, 3) },
                        { DXGI_FORMAT_BC3_UNORM, new BcPixFmt(ValueType.Unorm, AlphaType.None, 3) },
                        { DXGI_FORMAT_BC3_UNORM_SRGB, new BcPixFmt(ValueType.UnormSrgb, AlphaType.None, 3) },
                        { DXGI_FORMAT_BC4_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.None, 4) },
                        { DXGI_FORMAT_BC4_UNORM, new BcPixFmt(ValueType.Unorm, AlphaType.None, 4) },
                        { DXGI_FORMAT_BC4_SNORM, new BcPixFmt(ValueType.Snorm, AlphaType.None, 4) },
                        { DXGI_FORMAT_BC5_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.None, 5) },
                        { DXGI_FORMAT_BC5_UNORM, new BcPixFmt(ValueType.Unorm, AlphaType.None, 5) },
                        { DXGI_FORMAT_BC5_SNORM, new BcPixFmt(ValueType.Snorm, AlphaType.None, 5) },
                        { DXGI_FORMAT_BC6H_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.None, 6) },
                        { DXGI_FORMAT_BC6H_UF16, new BcPixFmt(ValueType.Uf16, AlphaType.None, 6) },
                        { DXGI_FORMAT_BC6H_SF16, new BcPixFmt(ValueType.Sf16, AlphaType.None, 6) },
                        { DXGI_FORMAT_BC7_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.None, 7) },
                        { DXGI_FORMAT_BC7_UNORM, new BcPixFmt(ValueType.Unorm, AlphaType.None, 7) },
                        { DXGI_FORMAT_BC7_UNORM_SRGB, new BcPixFmt(ValueType.UnormSrgb, AlphaType.None, 7) },
                    }
                }, {
                    AlphaType.Straight, new Dictionary<DXGI_FORMAT, IPixFmt> {
                        {
                            DXGI_FORMAT_R32G32B32A32_TYPELESS,
                            RgbaPixFmt.NewRgba(32, 32, 32, 32, 0, 0, ValueType.Typeless)
                        },
                        { DXGI_FORMAT_R32G32B32A32_FLOAT, RgbaPixFmt.NewRgba(32, 32, 32, 32, 0, 0, ValueType.Float) },
                        { DXGI_FORMAT_R32G32B32A32_UINT, RgbaPixFmt.NewRgba(32, 32, 32, 32, 0, 0, ValueType.Uint) },
                        { DXGI_FORMAT_R32G32B32A32_SINT, RgbaPixFmt.NewRgba(32, 32, 32, 32, 0, 0, ValueType.Sint) }, {
                            DXGI_FORMAT_R16G16B16A16_TYPELESS,
                            RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Typeless)
                        },
                        { DXGI_FORMAT_R16G16B16A16_FLOAT, RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Float) },
                        { DXGI_FORMAT_R16G16B16A16_UNORM, RgbaPixFmt.NewRgba(16, 16, 16, 16) },
                        { DXGI_FORMAT_R16G16B16A16_UINT, RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Uint) },
                        { DXGI_FORMAT_R16G16B16A16_SNORM, RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Snorm) },
                        { DXGI_FORMAT_R16G16B16A16_SINT, RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Sint) }, {
                            DXGI_FORMAT_R10G10B10A2_TYPELESS,
                            RgbaPixFmt.NewRgba(10, 10, 10, 2, 0, 0, ValueType.Typeless)
                        },
                        { DXGI_FORMAT_R10G10B10A2_UNORM, RgbaPixFmt.NewRgba(10, 10, 10, 2) },
                        { DXGI_FORMAT_R10G10B10A2_UINT, RgbaPixFmt.NewRgba(10, 10, 10, 2, 0, 0, ValueType.Uint) },
                        { DXGI_FORMAT_R8G8B8A8_TYPELESS, RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.Typeless) },
                        { DXGI_FORMAT_R8G8B8A8_UNORM, RgbaPixFmt.NewRgba(8, 8, 8, 8) },
                        { DXGI_FORMAT_R8G8B8A8_UNORM_SRGB, RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.UnormSrgb) },
                        { DXGI_FORMAT_R8G8B8A8_UINT, RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.Uint) },
                        { DXGI_FORMAT_R8G8B8A8_SNORM, RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.Snorm) },
                        { DXGI_FORMAT_R8G8B8A8_SINT, RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.Sint) },
                        { DXGI_FORMAT_A8_UNORM, RgbaPixFmt.NewA(8) },
                        { DXGI_FORMAT_B5G5R5A1_UNORM, RgbaPixFmt.NewBgra(5, 5, 5, 1) },
                        { DXGI_FORMAT_B8G8R8A8_UNORM, RgbaPixFmt.NewBgra(8, 8, 8, 8) },
                        { DXGI_FORMAT_B8G8R8A8_TYPELESS, RgbaPixFmt.NewBgra(8, 8, 8, 8, 0, 0, ValueType.Typeless) },
                        { DXGI_FORMAT_B8G8R8A8_UNORM_SRGB, RgbaPixFmt.NewBgra(8, 8, 8, 8, 0, 0, ValueType.UnormSrgb) },
                        { DXGI_FORMAT_B4G4R4A4_UNORM, RgbaPixFmt.NewBgra(4, 4, 4, 4) },
                        { DXGI_FORMAT_BC1_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.Straight, 1) },
                        { DXGI_FORMAT_BC1_UNORM, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 1) },
                        { DXGI_FORMAT_BC1_UNORM_SRGB, new BcPixFmt(ValueType.UnormSrgb, AlphaType.Straight, 1) },
                        { DXGI_FORMAT_BC2_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.Straight, 2) },
                        { DXGI_FORMAT_BC2_UNORM, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 2) },
                        { DXGI_FORMAT_BC2_UNORM_SRGB, new BcPixFmt(ValueType.UnormSrgb, AlphaType.Straight, 2) },
                        { DXGI_FORMAT_BC3_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.Straight, 3) },
                        { DXGI_FORMAT_BC3_UNORM, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 3) },
                        { DXGI_FORMAT_BC3_UNORM_SRGB, new BcPixFmt(ValueType.UnormSrgb, AlphaType.Straight, 3) },
                        { DXGI_FORMAT_BC4_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.Straight, 4) },
                        { DXGI_FORMAT_BC4_UNORM, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 4) },
                        { DXGI_FORMAT_BC4_SNORM, new BcPixFmt(ValueType.Snorm, AlphaType.Straight, 4) },
                        { DXGI_FORMAT_BC5_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.Straight, 5) },
                        { DXGI_FORMAT_BC5_UNORM, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 5) },
                        { DXGI_FORMAT_BC5_SNORM, new BcPixFmt(ValueType.Snorm, AlphaType.Straight, 5) },
                        { DXGI_FORMAT_BC6H_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.Straight, 6) },
                        { DXGI_FORMAT_BC6H_UF16, new BcPixFmt(ValueType.Uf16, AlphaType.Straight, 6) },
                        { DXGI_FORMAT_BC6H_SF16, new BcPixFmt(ValueType.Sf16, AlphaType.Straight, 6) },
                        { DXGI_FORMAT_BC7_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.Straight, 7) },
                        { DXGI_FORMAT_BC7_UNORM, new BcPixFmt(ValueType.Unorm, AlphaType.Straight, 7) },
                        { DXGI_FORMAT_BC7_UNORM_SRGB, new BcPixFmt(ValueType.UnormSrgb, AlphaType.Straight, 7) },
                    }
                }, {
                    AlphaType.Premultiplied, new Dictionary<DXGI_FORMAT, IPixFmt> {
                        {
                            DXGI_FORMAT_R32G32B32A32_TYPELESS,
                            RgbaPixFmt.NewRgba(32, 32, 32, 32, 0, 0, ValueType.Typeless, AlphaType.Premultiplied)
                        }, {
                            DXGI_FORMAT_R32G32B32A32_FLOAT,
                            RgbaPixFmt.NewRgba(32, 32, 32, 32, 0, 0, ValueType.Float, AlphaType.Premultiplied)
                        }, {
                            DXGI_FORMAT_R32G32B32A32_UINT,
                            RgbaPixFmt.NewRgba(32, 32, 32, 32, 0, 0, ValueType.Uint, AlphaType.Premultiplied)
                        }, {
                            DXGI_FORMAT_R32G32B32A32_SINT,
                            RgbaPixFmt.NewRgba(32, 32, 32, 32, 0, 0, ValueType.Sint, AlphaType.Premultiplied)
                        }, {
                            DXGI_FORMAT_R16G16B16A16_TYPELESS,
                            RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Typeless, AlphaType.Premultiplied)
                        }, {
                            DXGI_FORMAT_R16G16B16A16_FLOAT,
                            RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Float, AlphaType.Premultiplied)
                        }, {
                            DXGI_FORMAT_R16G16B16A16_UNORM,
                            RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Unorm, AlphaType.Premultiplied)
                        }, {
                            DXGI_FORMAT_R16G16B16A16_UINT,
                            RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Uint, AlphaType.Premultiplied)
                        }, {
                            DXGI_FORMAT_R16G16B16A16_SNORM,
                            RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Snorm, AlphaType.Premultiplied)
                        }, {
                            DXGI_FORMAT_R16G16B16A16_SINT,
                            RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Sint, AlphaType.Premultiplied)
                        }, {
                            DXGI_FORMAT_R10G10B10A2_TYPELESS,
                            RgbaPixFmt.NewRgba(10, 10, 10, 2, 0, 0, ValueType.Typeless, AlphaType.Premultiplied)
                        }, {
                            DXGI_FORMAT_R10G10B10A2_UNORM,
                            RgbaPixFmt.NewRgba(10, 10, 10, 2, 0, 0, ValueType.Unorm, AlphaType.Premultiplied)
                        }, {
                            DXGI_FORMAT_R10G10B10A2_UINT,
                            RgbaPixFmt.NewRgba(10, 10, 10, 2, 0, 0, ValueType.Uint, AlphaType.Premultiplied)
                        }, {
                            DXGI_FORMAT_R8G8B8A8_TYPELESS,
                            RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.Typeless, AlphaType.Premultiplied)
                        }, {
                            DXGI_FORMAT_R8G8B8A8_UNORM,
                            RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.Unorm, AlphaType.Premultiplied)
                        }, {
                            DXGI_FORMAT_R8G8B8A8_UNORM_SRGB,
                            RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.UnormSrgb, AlphaType.Premultiplied)
                        }, {
                            DXGI_FORMAT_R8G8B8A8_UINT,
                            RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.Uint, AlphaType.Premultiplied)
                        }, {
                            DXGI_FORMAT_R8G8B8A8_SNORM,
                            RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.Snorm, AlphaType.Premultiplied)
                        }, {
                            DXGI_FORMAT_R8G8B8A8_SINT,
                            RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.Sint, AlphaType.Premultiplied)
                        },
                        { DXGI_FORMAT_A8_UNORM, RgbaPixFmt.NewA(8, 0, 0, ValueType.Unorm, AlphaType.Premultiplied) }, {
                            DXGI_FORMAT_B5G5R5A1_UNORM,
                            RgbaPixFmt.NewBgra(5, 5, 5, 1, 0, 0, ValueType.Unorm, AlphaType.Premultiplied)
                        }, {
                            DXGI_FORMAT_B8G8R8A8_UNORM,
                            RgbaPixFmt.NewBgra(8, 8, 8, 8, 0, 0, ValueType.Unorm, AlphaType.Premultiplied)
                        }, {
                            DXGI_FORMAT_B8G8R8A8_TYPELESS,
                            RgbaPixFmt.NewBgra(8, 8, 8, 8, 0, 0, ValueType.Typeless, AlphaType.Premultiplied)
                        }, {
                            DXGI_FORMAT_B8G8R8A8_UNORM_SRGB,
                            RgbaPixFmt.NewBgra(8, 8, 8, 8, 0, 0, ValueType.UnormSrgb, AlphaType.Premultiplied)
                        }, {
                            DXGI_FORMAT_B4G4R4A4_UNORM,
                            RgbaPixFmt.NewBgra(4, 4, 4, 4, 0, 0, ValueType.Unorm, AlphaType.Premultiplied)
                        },
                        { DXGI_FORMAT_BC1_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.Premultiplied, 1) },
                        { DXGI_FORMAT_BC1_UNORM, new BcPixFmt(ValueType.Unorm, AlphaType.Premultiplied, 1) },
                        { DXGI_FORMAT_BC1_UNORM_SRGB, new BcPixFmt(ValueType.UnormSrgb, AlphaType.Premultiplied, 1) },
                        { DXGI_FORMAT_BC2_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.Premultiplied, 2) },
                        { DXGI_FORMAT_BC2_UNORM, new BcPixFmt(ValueType.Unorm, AlphaType.Premultiplied, 2) },
                        { DXGI_FORMAT_BC2_UNORM_SRGB, new BcPixFmt(ValueType.UnormSrgb, AlphaType.Premultiplied, 2) },
                        { DXGI_FORMAT_BC3_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.Premultiplied, 3) },
                        { DXGI_FORMAT_BC3_UNORM, new BcPixFmt(ValueType.Unorm, AlphaType.Premultiplied, 3) },
                        { DXGI_FORMAT_BC3_UNORM_SRGB, new BcPixFmt(ValueType.UnormSrgb, AlphaType.Premultiplied, 3) },
                        { DXGI_FORMAT_BC4_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.Premultiplied, 4) },
                        { DXGI_FORMAT_BC4_UNORM, new BcPixFmt(ValueType.Unorm, AlphaType.Premultiplied, 4) },
                        { DXGI_FORMAT_BC4_SNORM, new BcPixFmt(ValueType.Snorm, AlphaType.Premultiplied, 4) },
                        { DXGI_FORMAT_BC5_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.Premultiplied, 5) },
                        { DXGI_FORMAT_BC5_UNORM, new BcPixFmt(ValueType.Unorm, AlphaType.Premultiplied, 5) },
                        { DXGI_FORMAT_BC5_SNORM, new BcPixFmt(ValueType.Snorm, AlphaType.Premultiplied, 5) },
                        { DXGI_FORMAT_BC6H_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.Premultiplied, 6) },
                        { DXGI_FORMAT_BC6H_UF16, new BcPixFmt(ValueType.Uf16, AlphaType.Premultiplied, 6) },
                        { DXGI_FORMAT_BC6H_SF16, new BcPixFmt(ValueType.Sf16, AlphaType.Premultiplied, 6) },
                        { DXGI_FORMAT_BC7_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.Premultiplied, 7) },
                        { DXGI_FORMAT_BC7_UNORM, new BcPixFmt(ValueType.Unorm, AlphaType.Premultiplied, 7) },
                        { DXGI_FORMAT_BC7_UNORM_SRGB, new BcPixFmt(ValueType.UnormSrgb, AlphaType.Premultiplied, 7) },
                    }
                }, {
                    AlphaType.Custom, new Dictionary<DXGI_FORMAT, IPixFmt> {
                        {
                            DXGI_FORMAT_B8G8R8X8_UNORM,
                            RgbaPixFmt.NewBgr(8, 8, 8, 8, 0, ValueType.Unorm, AlphaType.Custom)
                        }, {
                            DXGI_FORMAT_B8G8R8X8_TYPELESS,
                            RgbaPixFmt.NewBgr(8, 8, 8, 8, 0, ValueType.Typeless, AlphaType.Custom)
                        }, {
                            DXGI_FORMAT_B8G8R8X8_UNORM_SRGB,
                            RgbaPixFmt.NewBgr(8, 8, 8, 8, 0, ValueType.UnormSrgb, AlphaType.Custom)
                        },
                        { DXGI_FORMAT_BC1_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.Custom, 1) },
                        { DXGI_FORMAT_BC1_UNORM, new BcPixFmt(ValueType.Unorm, AlphaType.Custom, 1) },
                        { DXGI_FORMAT_BC1_UNORM_SRGB, new BcPixFmt(ValueType.UnormSrgb, AlphaType.Custom, 1) },
                        { DXGI_FORMAT_BC2_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.Custom, 2) },
                        { DXGI_FORMAT_BC2_UNORM, new BcPixFmt(ValueType.Unorm, AlphaType.Custom, 2) },
                        { DXGI_FORMAT_BC2_UNORM_SRGB, new BcPixFmt(ValueType.UnormSrgb, AlphaType.Custom, 2) },
                        { DXGI_FORMAT_BC3_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.Custom, 3) },
                        { DXGI_FORMAT_BC3_UNORM, new BcPixFmt(ValueType.Unorm, AlphaType.Custom, 3) },
                        { DXGI_FORMAT_BC3_UNORM_SRGB, new BcPixFmt(ValueType.UnormSrgb, AlphaType.Custom, 3) },
                        { DXGI_FORMAT_BC4_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.Custom, 4) },
                        { DXGI_FORMAT_BC4_UNORM, new BcPixFmt(ValueType.Unorm, AlphaType.Custom, 4) },
                        { DXGI_FORMAT_BC4_SNORM, new BcPixFmt(ValueType.Snorm, AlphaType.Custom, 4) },
                        { DXGI_FORMAT_BC5_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.Custom, 5) },
                        { DXGI_FORMAT_BC5_UNORM, new BcPixFmt(ValueType.Unorm, AlphaType.Custom, 5) },
                        { DXGI_FORMAT_BC5_SNORM, new BcPixFmt(ValueType.Snorm, AlphaType.Custom, 5) },
                        { DXGI_FORMAT_BC6H_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.Custom, 6) },
                        { DXGI_FORMAT_BC6H_UF16, new BcPixFmt(ValueType.Uf16, AlphaType.Custom, 6) },
                        { DXGI_FORMAT_BC6H_SF16, new BcPixFmt(ValueType.Sf16, AlphaType.Custom, 6) },
                        { DXGI_FORMAT_BC7_TYPELESS, new BcPixFmt(ValueType.Typeless, AlphaType.Custom, 7) },
                        { DXGI_FORMAT_BC7_UNORM, new BcPixFmt(ValueType.Unorm, AlphaType.Custom, 7) },
                        { DXGI_FORMAT_BC7_UNORM_SRGB, new BcPixFmt(ValueType.UnormSrgb, AlphaType.Custom, 7) },
                    }
                },
            };

        // https://learn.microsoft.com/en-us/windows/win32/wic/-wic-codec-native-pixel-formats#packed-bit-pixel-formats
        // Note: below list might got byte orders wrong (Bgr/Rgb)
        WicToPixelFormat = new Dictionary<Guid, IPixFmt> {
            // Packed Bit Pixel Formats
            {
                GUID.GUID_WICPixelFormat16bppBGR555,
                RgbaPixFmt.NewBgr(5, 5, 5, 1, 0, ValueType.Unorm, AlphaType.None)
            }, {
                GUID.GUID_WICPixelFormat16bppBGR565,
                RgbaPixFmt.NewBgr(5, 6, 5, 0, 0, ValueType.Unorm, AlphaType.None)
            }, {
                GUID.GUID_WICPixelFormat16bppBGRA5551,
                RgbaPixFmt.NewBgra(5, 5, 5, 1, 0, 0, ValueType.Unorm, AlphaType.None)
            }, {
                GUID.GUID_WICPixelFormat32bppBGR101010,
                RgbaPixFmt.NewBgr(10, 10, 10, 2, 0, ValueType.Unorm, AlphaType.None)
            }, {
                GUID.GUID_WICPixelFormat32bppRGBA1010102,
                RgbaPixFmt.NewRgba(10, 10, 10, 2, 0, 0, ValueType.Unorm, AlphaType.None)
            }, {
                GUID.GUID_WICPixelFormat32bppR10G10B10A2,
                RgbaPixFmt.NewBgra(10, 10, 10, 2, 0, 0, ValueType.Unorm, AlphaType.None)
            },

            // Grayscale Pixel Formats
            { GUID.GUID_WICPixelFormatBlackWhite, new LumiPixFmt(AlphaType.None, new(ValueType.Unorm, 0, 1)) },
            { GUID.GUID_WICPixelFormat2bppGray, new LumiPixFmt(AlphaType.None, new(ValueType.Unorm, 0, 2)) },
            { GUID.GUID_WICPixelFormat4bppGray, new LumiPixFmt(AlphaType.None, new(ValueType.Unorm, 0, 4)) },
            { GUID.GUID_WICPixelFormat8bppGray, new LumiPixFmt(AlphaType.None, new(ValueType.Unorm, 0, 8)) },
            { GUID.GUID_WICPixelFormat16bppGray, new LumiPixFmt(AlphaType.None, new(ValueType.Unorm, 0, 16)) }, {
                GUID.GUID_WICPixelFormat16bppGrayHalf,
                new LumiPixFmt(AlphaType.None, new(ValueType.Half, 0, 16))
            }, {
                GUID.GUID_WICPixelFormat32bppGrayFloat,
                new LumiPixFmt(AlphaType.None, new(ValueType.Float, 0, 32))
            },

            // RGB/BGR Pixel formats
            {
                GUID.GUID_WICPixelFormat24bppRGB,
                RgbaPixFmt.NewRgb(8, 8, 8, 0, 0, ValueType.Unorm, AlphaType.None)
            }, {
                GUID.GUID_WICPixelFormat24bppBGR,
                RgbaPixFmt.NewBgr(8, 8, 8, 0, 0, ValueType.Unorm, AlphaType.None)
            }, {
                GUID.GUID_WICPixelFormat32bppBGR,
                RgbaPixFmt.NewBgr(8, 8, 8, 8, 0, ValueType.Unorm, AlphaType.None)
            }, {
                GUID.GUID_WICPixelFormat32bppRGBA,
                RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.Unorm, AlphaType.Straight)
            }, {
                GUID.GUID_WICPixelFormat32bppBGRA,
                RgbaPixFmt.NewBgra(8, 8, 8, 8, 0, 0, ValueType.Unorm, AlphaType.Straight)
            }, {
                GUID.GUID_WICPixelFormat32bppPRGBA,
                RgbaPixFmt.NewRgba(8, 8, 8, 8, 0, 0, ValueType.Unorm, AlphaType.Premultiplied)
            }, {
                GUID.GUID_WICPixelFormat32bppPBGRA,
                RgbaPixFmt.NewBgra(8, 8, 8, 8, 0, 0, ValueType.Unorm, AlphaType.Premultiplied)
            }, {
                GUID.GUID_WICPixelFormat48bppRGB,
                RgbaPixFmt.NewRgba(16, 16, 16, 0, 0, 0, ValueType.Unorm, AlphaType.None)
            }, {
                GUID.GUID_WICPixelFormat48bppBGR,
                RgbaPixFmt.NewBgra(16, 16, 16, 0, 0, 0, ValueType.Unorm, AlphaType.None)
            }, {
                GUID.GUID_WICPixelFormat48bppRGBHalf,
                RgbaPixFmt.NewRgba(16, 16, 16, 0, 0, 0, ValueType.Half, AlphaType.None)
            }, {
                GUID.GUID_WICPixelFormat64bppRGBA,
                RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Unorm, AlphaType.Straight)
            }, {
                GUID.GUID_WICPixelFormat64bppBGRA,
                RgbaPixFmt.NewBgra(16, 16, 16, 16, 0, 0, ValueType.Unorm, AlphaType.Straight)
            }, {
                GUID.GUID_WICPixelFormat64bppPRGBA,
                RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Unorm, AlphaType.Premultiplied)
            }, {
                GUID.GUID_WICPixelFormat64bppPBGRA,
                RgbaPixFmt.NewBgra(16, 16, 16, 16, 0, 0, ValueType.Unorm, AlphaType.Premultiplied)
            }, {
                GUID.GUID_WICPixelFormat64bppRGBHalf,
                RgbaPixFmt.NewRgb(16, 16, 16, 16, 0, ValueType.Half, AlphaType.None)
            }, {
                GUID.GUID_WICPixelFormat64bppRGBAHalf,
                RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Half, AlphaType.None)
            }, {
                GUID.GUID_WICPixelFormat128bppRGBFloat,
                RgbaPixFmt.NewRgb(32, 32, 32, 32, 0, ValueType.Float, AlphaType.None)
            }, {
                GUID.GUID_WICPixelFormat128bppRGBAFloat,
                RgbaPixFmt.NewRgba(32, 32, 32, 32, 0, 0, ValueType.Float, AlphaType.Straight)
            }, {
                GUID.GUID_WICPixelFormat128bppPRGBAFloat,
                RgbaPixFmt.NewRgba(32, 32, 32, 32, 0, 0, ValueType.Float, AlphaType.Premultiplied)
            },

            // RGB/BGR Pixel formats (Windows 8 & Platform Update for Windows 7)
            {
                GUID.GUID_WICPixelFormat32bppRGB,
                RgbaPixFmt.NewRgb(8, 8, 8, 8, 0, ValueType.Unorm, AlphaType.None)
            }, {
                GUID.GUID_WICPixelFormat64bppRGB,
                RgbaPixFmt.NewRgb(16, 16, 16, 16, 0, ValueType.Unorm, AlphaType.None)
            }, {
                GUID.GUID_WICPixelFormat96bppRGBFloat,
                RgbaPixFmt.NewRgb(32, 32, 32, 0, 0, ValueType.Float, AlphaType.None)
            }, {
                GUID.GUID_WICPixelFormat64bppPRGBAHalf,
                RgbaPixFmt.NewRgba(16, 16, 16, 16, 0, 0, ValueType.Half, AlphaType.Premultiplied)
            },
        };
    }

    public static IPixFmt GetPixelFormat(DdsFourCc fourCc) =>
        FourCcToPixelFormat.TryGetValue(fourCc, out var v) ? v : UnknownPixFmt.Instance;

    public static IPixFmt GetPixelFormat(AlphaType alphaType, DXGI_FORMAT dxgiFormat) =>
        DxgiFormatToPixelFormat.TryGetValue(alphaType, out var d1)
            ? d1.TryGetValue(dxgiFormat, out var pf)
                ? pf
                : UnknownPixFmt.Instance
            : UnknownPixFmt.Instance;

    public static IPixFmt GetPixelFormat(Guid pixelFormatGuid) =>
        WicToPixelFormat.TryGetValue(pixelFormatGuid, out var v) ? v : UnknownPixFmt.Instance;

    public static DdsFourCc GetFourCc(IPixFmt pf) =>
        FourCcToPixelFormat.FirstOrDefault(x => Equals(x.Value, pf)).Key;

    public static DXGI_FORMAT GetDxgiFormat(IPixFmt pf) =>
        DxgiFormatToPixelFormat.TryGetValue(pf.Alpha, out var d1)
            ? d1.FirstOrDefault(x => Equals(x.Value, pf)).Key
            : DXGI_FORMAT_UNKNOWN;

    public static Guid GetWicPixelFormat(IPixFmt pf)
    {
        var r = WicToPixelFormat.FirstOrDefault(x => Equals(x.Value, pf)).Key;
        return r == Guid.Empty ? GUID.GUID_WICPixelFormatUndefined : r;
    }
}
