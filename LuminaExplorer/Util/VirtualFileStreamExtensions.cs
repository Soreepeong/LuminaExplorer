using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Tex.Buffers;
using LuminaExplorer.LazySqPackTree.VirtualFileStream;

namespace LuminaExplorer.Util; 

public static class VirtualFileStreamExtensions {
    public static TexFile.TexHeader ExtractTexHeader(this BaseVirtualFileStream stream) {
        if (stream is TextureVirtualFileStream tvfs)
            return tvfs.TexHeader;

        return new LuminaBinaryReader(((Stream) stream.Clone()).WithSeek(0), stream.PlatformId)
            .ReadStructure<TexFile.TexHeader>();
    }
    
    public static TextureBuffer ExtractMipmapOfSizeAtLeast(this BaseVirtualFileStream stream, int minEdgeLength) {
        var header = stream.ExtractTexHeader();
        var level = 0;
        while (level < header.MipLevels - 1 &&
               (header.Width >> (level + 1)) >= minEdgeLength &&
               (header.Height >> (level + 1)) >= minEdgeLength)
            level++;
        return stream.ExtractMipmap(level);
    }

    public static unsafe TextureBuffer ExtractMipmap(this BaseVirtualFileStream stream, int level) {
        var header = stream.ExtractTexHeader();
        if (level < 0 || level >= header.MipLevels)
            throw new ArgumentOutOfRangeException(nameof(level), level, null);
        
        var offset = header.OffsetToSurface[level];
        var length = (int)((level == header.MipLevels - 1 ? stream.Length : header.OffsetToSurface[level + 1]) - offset);
        var buffer = new byte[length];
        ((Stream)stream.Clone()).WithSeek(offset).ReadFully(new(buffer));
        
        var mipWidth = Math.Max(1, header.Width >> level);
        var mipHeight = Math.Max(1, header.Height >> level);
        var mipDepth = Math.Max(1, header.Depth >> level);
        return TextureBuffer.FromTextureFormat(
            header.Type,
            header.Format,
            mipWidth,
            mipHeight,
            mipDepth,
            new[] {length},
            buffer,
            stream.PlatformId);
    }

}
