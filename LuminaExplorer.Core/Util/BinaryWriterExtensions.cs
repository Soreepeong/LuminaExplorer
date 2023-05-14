using System;
using System.IO;

namespace LuminaExplorer.Core.Util; 

public static class BinaryWriterExtensions {
    public static T WithSeek<T>(this T reader, long offset, SeekOrigin origin = SeekOrigin.Begin)
        where T : BinaryWriter {
        var position = reader.BaseStream.Position;
        var newOffset = origin switch {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => position + offset,
            SeekOrigin.End => position + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null)
        };
        if (position != newOffset)
            reader.BaseStream.Position = newOffset;
        return reader;
    }

    public static T WithAlign<T>(this T reader, int unit) where T : BinaryWriter {
        reader.BaseStream.Position = (reader.BaseStream.Position + unit - 1) / unit * unit;
        return reader;
    }
}
