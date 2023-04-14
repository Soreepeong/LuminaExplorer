namespace LuminaExplorer.Util; 

public static class StreamExtensions {
    public static void ReadFully(this Stream stream, Span<byte> buffer) {
        var i = 0;
        while (i < buffer.Length) {
            var r = stream.Read(buffer[i..]);
            if (r == 0)
                throw new IOException("Failed to read fully");
            i += r;
        }
    }

    public static Stream SeekIfNecessary(this Stream stream, long absoluteOffset) {
        if (stream.Position != absoluteOffset)
            stream.Position = absoluteOffset;
        return stream;
    }
}
