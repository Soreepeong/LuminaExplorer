using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LuminaExplorer.Core.Util;

public sealed class CountingStream : Stream {
    private readonly Stream _innerStream;

    public CountingStream(Stream innerStream)
    {
        this._innerStream = innerStream;
    }

    public long WriteCounter { get; private set; }
    public long ReadCounter { get; private set; }

    public override void Flush() => this._innerStream.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = this._innerStream.Read(buffer, offset, count);
        this.ReadCounter += n;
        return n;
    }

    public override long Seek(long offset, SeekOrigin origin) => this._innerStream.Seek(offset, origin);

    public override void SetLength(long value) => this._innerStream.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
    {
        this._innerStream.Write(buffer, offset, count);
        this.WriteCounter += count;
    }

    public override bool CanRead => this._innerStream.CanRead;

    public override bool CanSeek => this._innerStream.CanSeek;

    public override bool CanWrite => this._innerStream.CanWrite;

    public override long Length => this._innerStream.Length;

    public override long Position {
        get => this._innerStream.Position;
        set => this._innerStream.Position = value;
    }

    public override bool CanTimeout => false;

    public override int ReadTimeout {
        get => this._innerStream.ReadTimeout;
        set => this._innerStream.ReadTimeout = value;
    }

    public override int WriteTimeout {
        get => this._innerStream.WriteTimeout;
        set => this._innerStream.WriteTimeout = value;
    }

    public override void Close() => this._innerStream.Close();

    protected override void Dispose(bool disposing) => this._innerStream.Dispose();

    public override ValueTask DisposeAsync() => this._innerStream.DisposeAsync();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        this._innerStream.FlushAsync(cancellationToken);

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        var n = await this._innerStream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        this.ReadCounter += n;
        return n;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var n = await this._innerStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        this.ReadCounter += n;
        return n;
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await this._innerStream.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        this.WriteCounter += count;
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        await this._innerStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        this.WriteCounter += buffer.Length;
    }

    public override int Read(Span<byte> buffer)
    {
        var r = this._innerStream.Read(buffer);
        this.ReadCounter += r;
        return r;
    }

    public override int ReadByte()
    {
        var r = this._innerStream.ReadByte();
        if (r >= 0) this.ReadCounter++;
        return r;
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        this._innerStream.Write(buffer);
        this.WriteCounter += buffer.Length;
    }

    public override void WriteByte(byte value)
    {
        this._innerStream.WriteByte(value);
        this.WriteCounter++;
    }
}
