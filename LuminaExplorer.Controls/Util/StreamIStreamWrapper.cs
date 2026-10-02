using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;

namespace LuminaExplorer.Controls.Util;

public class StreamIStreamWrapper : Stream, ICloneable, IStream {
    public Stream BaseStream;

    private RefCounter? _refCounter;

    public StreamIStreamWrapper(Stream baseStream, bool leaveOpen = false)
    {
        this.BaseStream = baseStream;
        this._refCounter = new(leaveOpen);
    }

    private StreamIStreamWrapper(StreamIStreamWrapper cloneFrom)
    {
        this._refCounter = cloneFrom._refCounter;
        if (this._refCounter is null)
            throw new ObjectDisposedException("Object already disposed");
        this._refCounter.AddRef();
        this.BaseStream = cloneFrom.BaseStream;
    }

    #region Stream

    public override bool CanRead => this.BaseStream.CanRead;

    public override bool CanSeek => this.BaseStream.CanSeek;

    public override bool CanWrite => this.BaseStream.CanWrite;

    public override bool CanTimeout => this.BaseStream.CanTimeout;

    public override long Length => this.BaseStream.Length;

    public override long Position {
        get => this.BaseStream.Position;
        set => this.BaseStream.Position = value;
    }

    public override int ReadTimeout {
        get => this.BaseStream.ReadTimeout;
        set => this.BaseStream.ReadTimeout = value;
    }

    public override int WriteTimeout {
        get => this.BaseStream.WriteTimeout;
        set => this.BaseStream.WriteTimeout = value;
    }

    public override void CopyTo(Stream destination, int bufferSize) => this.BaseStream.CopyTo(destination, bufferSize);

    public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) =>
        this.BaseStream.CopyToAsync(destination, bufferSize, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (this._refCounter is null)
            throw new ObjectDisposedException("Object already disposed");

        if (this._refCounter.Release(out var leaveOpen) == 0 && !leaveOpen) this.BaseStream.Dispose();
        this.BaseStream = null!;
        this._refCounter = null;
    }

    public override ValueTask DisposeAsync()
    {
        if (this._refCounter is null)
            throw new ObjectDisposedException("Object already disposed");

        GC.SuppressFinalize(this);
        var disposeValueTask = this._refCounter.Release(out var leaveOpen) == 0 && !leaveOpen
            ? this.BaseStream.DisposeAsync()
            : ValueTask.CompletedTask;
        this.BaseStream = null!;
        this._refCounter = null;
        return disposeValueTask;
    }

    public override void Flush() => this.BaseStream.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        this.BaseStream.FlushAsync(cancellationToken);

    public override IAsyncResult
        BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state) =>
        this.BaseStream.BeginRead(buffer, offset, count, callback, state);

    public override int EndRead(IAsyncResult asyncResult) => this.BaseStream.EndRead(asyncResult);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        this.BaseStream.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = new()) =>
        this.BaseStream.ReadAsync(buffer, cancellationToken);

    public override IAsyncResult BeginWrite(
        byte[] buffer,
        int offset,
        int count,
        AsyncCallback? callback,
        object? state) =>
        this.BaseStream.BeginWrite(buffer, offset, count, callback, state);

    public override void EndWrite(IAsyncResult asyncResult) => this.BaseStream.EndWrite(asyncResult);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        this.BaseStream.WriteAsync(buffer, offset, count, cancellationToken);

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = new()) =>
        this.BaseStream.WriteAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => this.BaseStream.Seek(offset, origin);

    public override void SetLength(long value) => this.BaseStream.SetLength(value);

    public override int Read(byte[] buffer, int offset, int count) => this.BaseStream.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => this.BaseStream.Read(buffer);

    public override int ReadByte() => this.BaseStream.ReadByte();

    public override void Write(byte[] buffer, int offset, int count) => this.BaseStream.Write(buffer, offset, count);

    public override void Write(ReadOnlySpan<byte> buffer) => this.BaseStream.Write(buffer);

    public override void WriteByte(byte value) => this.BaseStream.WriteByte(value);

    #endregion

    #region ICloneable

    public object Clone() => new StreamIStreamWrapper(this);

    #endregion

    #region IStream

    public void Clone(out IStream ppstm) => ppstm = new StreamIStreamWrapper(this);

    public void Commit(int grfCommitFlags) => throw new NotImplementedException();

    public unsafe void CopyTo(IStream pstm, long cbLong, nint pcbReadUntyped, nint pcbWrittenUntyped)
    {
        var totalRead = 0ul;
        var totalWritten = 0ul;
        var cb = unchecked((ulong) cbLong);

        var buffer = ArrayPool<byte>.Shared.Rent(unchecked((int) Math.Min(cb, 0x1000)));
        try {
            while (cb != totalRead) {
                var read = (ulong) this.BaseStream.Read(buffer, 0, unchecked((int) Math.Min(cb - totalRead, 0x1000)));
                if (read == 0)
                    break;
                totalRead += read;

                var twritten = 0u;
                while (read > 0) {
                    pstm.Write(buffer, (int) read, new(&twritten));
                    if (twritten == 0)
                        return;
                    totalWritten += twritten;
                    read -= twritten;
                }
            }
        } finally {
            ArrayPool<byte>.Shared.Return(buffer);
            if (pcbReadUntyped != 0)
                *(ulong*) pcbReadUntyped = totalRead;
            if (pcbWrittenUntyped != 0)
                *(ulong*) pcbWrittenUntyped = totalWritten;
        }
    }

    public void LockRegion(long libOffset, long cb, int dwLockType) => throw new NotImplementedException();

    public unsafe void Read(byte[] pv, int cb, nint pcbRead)
    {
        var offset = 0;
        while (offset != cb) {
            var read = this.BaseStream.Read(pv, offset, cb - offset);
            if (read == 0)
                break;
            offset += read;
        }

        if (pcbRead != 0)
            *(uint*) pcbRead = (uint) offset;
    }

    public void Revert() => throw new NotImplementedException();

    public unsafe void Seek(long dlibMove, int dwOrigin, nint plibNewPosition)
    {
        var newPosition = this.BaseStream.Seek(dlibMove, (SeekOrigin) dwOrigin);
        if (plibNewPosition != 0)
            *(long*) plibNewPosition = newPosition;
    }

    public void SetSize(long libNewSize) => this.BaseStream.SetLength(libNewSize);

    public unsafe void Stat(out STATSTG pstatstg, int grfStatFlag)
    {
        pstatstg = new();
        switch (this.BaseStream) {
            case FileStream fs:
                pstatstg.pwcsName = fs.Name;
                try {
                    var fi = new FileInfo(fs.Name);
                    fixed (void* pmtime = &pstatstg.mtime)
                        *(long*) pmtime = fi.LastWriteTime.ToFileTime();
                    fixed (void* pctime = &pstatstg.ctime)
                        *(long*) pctime = fi.CreationTime.ToFileTime();
                    fixed (void* patime = &pstatstg.atime)
                        *(long*) patime = fi.LastAccessTime.ToFileTime();
                } catch (Exception) {
                    // ignore
                }

                pstatstg.type = 1; // STGTY_STORAGE
                break;
            case MemoryStream:
                pstatstg.type = 3; // STGTY_LOCKBYTES
                break;
            default:
                pstatstg.type = 2; // STGTY_STREAM
                break;
        }

        try {
            pstatstg.cbSize = this.BaseStream.Length;
        } catch (NotSupportedException) {
            throw new NotSupportedException();
        }
    }

    public void UnlockRegion(long libOffset, long cb, int dwLockType) => throw new NotImplementedException();

    public unsafe void Write(byte[] pv, int cb, nint pcbWritten)
    {
        this.BaseStream.Write(pv, 0, cb);
        if (pcbWritten != 0)
            *(uint*) pcbWritten = (uint) cb;
    }

    #endregion

    private sealed class RefCounter {
        private readonly bool _leaveOpen;
        private int _ref = 1;

        public RefCounter(bool leaveOpen)
        {
            this._leaveOpen = leaveOpen;
        }

        public int AddRef() => Interlocked.Increment(ref this._ref);

        public int Release(out bool leaveOpen)
        {
            leaveOpen = this._leaveOpen;
            return Interlocked.Decrement(ref this._ref);
        }
    }
}
