namespace LuminaExplorer.Util; 

public static class StreamAndBinaryRwExtensions {
    public static void ReadFully(this Stream stream, Span<byte> buffer) {
        var i = 0;
        while (i < buffer.Length) {
            var r = stream.Read(buffer[i..]);
            if (r == 0)
                throw new IOException("Failed to read fully");
            i += r;
        }
    }

    public static void ReadFully(this BinaryReader stream, Span<byte> buffer) {
        ReadFully(stream.BaseStream, buffer);
    }

    public static Stream WithSeek(this Stream stream, long absoluteOffset) {
        if (stream.Position != absoluteOffset)
            stream.Position = absoluteOffset;
        return stream;
    }

    public static BinaryReader WithSeek(this BinaryReader reader, long absoluteOffset) {
        reader.BaseStream.WithSeek(absoluteOffset);
        return reader;
    }
}
