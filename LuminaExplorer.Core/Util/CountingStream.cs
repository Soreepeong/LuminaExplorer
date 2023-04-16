namespace LuminaExplorer.Core.Util;

public class CountingStream : Stream {
    private readonly Stream _innerStream;

    public CountingStream(Stream innerStream) {
        _innerStream = innerStream;
    }

    public long WriteCounter { get; private set; }
    public long ReadCounter { get; private set; }

    public override void Flush() => _innerStream.Flush();

    public override int Read(byte[] buffer, int offset, int count) {
        var n = _innerStream.Read(buffer, offset, count);
        ReadCounter += n;
        return n;
    }

    public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);

    public override void SetLength(long value) => _innerStream.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count) {
        _innerStream.Write(buffer, offset, count);
        WriteCounter += count;
    }

    public override bool CanRead => _innerStream.CanRead;

    public override bool CanSeek => _innerStream.CanSeek;

    public override bool CanWrite => _innerStream.CanWrite;

    public override long Length => _innerStream.Length;

    public override long Position {
        get => _innerStream.Position;
        set => _innerStream.Position = value;
    }

    public override bool CanTimeout => false;

    public override int ReadTimeout {
        get => _innerStream.ReadTimeout;
        set => _innerStream.ReadTimeout = value;
    }

    public override int WriteTimeout {
        get => _innerStream.WriteTimeout;
        set => _innerStream.WriteTimeout = value;
    }

    public override void Close() => _innerStream.Close();

    protected override void Dispose(bool disposing) => _innerStream.Dispose();

    public override ValueTask DisposeAsync() {
        GC.SuppressFinalize(this);
        return _innerStream.DisposeAsync();
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => _innerStream.FlushAsync(cancellationToken);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _innerStream.ReadAsync(buffer, offset, count, cancellationToken).ContinueWith(x => {
            ReadCounter += x.Result;
            return x.Result;
        }, cancellationToken);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new(
        _innerStream.ReadAsync(buffer, cancellationToken).AsTask().ContinueWith(x => {
            ReadCounter += x.Result;
            return x.Result;
        }, cancellationToken));

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
        return _innerStream.WriteAsync(buffer, offset, count, cancellationToken)
            .ContinueWith(_ => WriteCounter += count, cancellationToken);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        new(_innerStream.WriteAsync(buffer, cancellationToken).AsTask()
            .ContinueWith(_ => WriteCounter += buffer.Length, cancellationToken));

    public override int Read(Span<byte> buffer) {
        var r = _innerStream.Read(buffer);
        ReadCounter += r;
        return r;
    }

    public override int ReadByte() {
        var r = _innerStream.ReadByte();
        if (r >= 0)
            ReadCounter++;
        return r;
    }

    public override void Write(ReadOnlySpan<byte> buffer) {
        _innerStream.Write(buffer);
        WriteCounter += buffer.Length;
    }

    public override void WriteByte(byte value) {
        _innerStream.WriteByte(value);
        WriteCounter++;
    }
}
