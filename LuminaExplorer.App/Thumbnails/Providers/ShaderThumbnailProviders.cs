using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

namespace LuminaExplorer.App.Thumbnails.Providers;

/// <summary>Shader packages: counts from the file header only, as the files can be very large.</summary>
public sealed class ShpkThumbnailProvider : IThumbnailProvider {
    public int Priority => 90;

    public ThumbnailCost Cost => ThumbnailCost.Cheap;

    public bool CanHandle(ThumbnailRequest request) => request.IsPossibly<ShpkFile>();

    public async Task<ThumbnailResult?> CreateAsync(
        ThumbnailRequest request,
        ThumbnailContext context,
        CancellationToken cancellationToken)
    {
        var headerSize = Marshal.SizeOf<ShpkHeader>();
        var buf = new byte[headerSize + 16];
        int read;
        await using (var stream = request.Lookup.CreateStream())
            read = await stream.ReadAtLeastAsync(buf, headerSize, false, cancellationToken);
        if (read < headerSize)
            return null;

        var header = MemoryMarshal.Read<ShpkHeader>(buf);
        if (header.Magic != ShpkHeader.MagicValue)
            return null;

        uint hs = 0, ds = 0, gs = 0;
        if (header.Version >= (uint) ShpkVersion.V0D01 && read >= headerSize + 12) {
            hs = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(headerSize));
            ds = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(headerSize + 4));
            gs = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(headerSize + 8));
        }

        return new InfoCard("SHPK", request.DisplayStem)
            .AddLine($"VS {header.VertexShaderCount} · PS {header.PixelShaderCount}", true)
            .AddLine(hs + ds + gs == 0 ? null : $"HS {hs} · DS {ds} · GS {gs}")
            .AddLine($"{header.NodeCount} nodes" + (header.NodeAliasCount == 0 ? "" : $" (+{header.NodeAliasCount})"))
            .AddLine($"{header.MaterialParamCount} material params")
            .AddLine($"Keys {header.SystemKeyCount}/{header.SceneKeyCount}/{header.MaterialKeyCount}")
            .AddLine($"{FormatDirectXVersion(header.DirectXVersion)} · v{header.Version:X4}")
            .Render(request.Settings);
    }

    public static string FormatDirectXVersion(DirectXVersion v) => v switch {
        DirectXVersion.Dx9 => "DX9",
        DirectXVersion.Dx10 => "DX10",
        DirectXVersion.Dx11 => "DX11",
        _ => $"0x{(uint) v:X8}",
    };
}

/// <summary>Shader code files: shader type and inputs.</summary>
public sealed class ShcdThumbnailProvider : IThumbnailProvider {
    public int Priority => 90;

    public ThumbnailCost Cost => ThumbnailCost.Cheap;

    public bool CanHandle(ThumbnailRequest request) => request.IsPossibly<ShcdFile>();

    public async Task<ThumbnailResult?> CreateAsync(
        ThumbnailRequest request,
        ThumbnailContext context,
        CancellationToken cancellationToken)
    {
        // Usually a few kilobytes, but do not load a huge file just for a thumbnail.
        if (request.Size > 16 << 20)
            throw new InvalidDataException("File too big");

        var shcd = await request.Lookup.AsFileResource<ShcdFile>(cancellationToken);
        var header = shcd.Header;
        return new InfoCard("SHCD", request.DisplayStem) {
                Glyph = shcd.ShaderType switch {
                    ShaderType.Vertex => "VS",
                    ShaderType.Pixel => "PS",
                    ShaderType.Geometry => "GS",
                    ShaderType.Compute => "CS",
                    ShaderType.HullShader => "HS",
                    ShaderType.DomainShader => "DS",
                    _ => $"0x{(ushort) shcd.ShaderType:X4}",
                },
            }
            .AddLine($"{shcd.InputNames.Length} inputs", true)
            .AddLine(
                $"C{header.ConstantCount} S{header.SamplerCount} T{header.TextureCount} U{header.UavCount}")
            .AddLine($"{shcd.ByteCode.Length:N0} bytes")
            .AddLine(
                $"{ShpkThumbnailProvider.FormatDirectXVersion(shcd.FileHeader.DirectXVersion)} · " +
                $"v{(ushort) shcd.FileHeader.Version:X4}")
            .Render(request.Settings);
    }
}
