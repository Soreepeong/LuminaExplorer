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

    public StreamIStreamWrapper(Stream baseStream, bool leaveOpen = false) {
        BaseStream = baseStream;
        _refCounter = new(leaveOpen);
    }

    private StreamIStreamWrapper(StreamIStreamWrapper cloneFrom) {
        _refCounter = cloneFrom._refCounter;
        if (_refCounter is null)
            throw new ObjectDisposedException("Object already disposed");
        _refCounter.AddRef();
        BaseStream = cloneFrom.BaseStream;
    }

    #region Stream

    public override bool CanRead => BaseStream.CanRead;

    public override bool CanSeek => BaseStream.CanSeek;

    public override bool CanWrite => BaseStream.CanWrite;

    public override bool CanTimeout => BaseStream.CanTimeout;

    public override long Length => BaseStream.Length;

    public override long Position {
        get => BaseStream.Position;
        set => BaseStream.Position = value;
    }

    public override int ReadTimeout {
        get => BaseStream.ReadTimeout;
        set => BaseStream.ReadTimeout = value;
    }

    public override int WriteTimeout {
        get => BaseStream.WriteTimeout;
        set => BaseStream.WriteTimeout = value;
    }

    public override void CopyTo(Stream destination, int bufferSize) => BaseStream.CopyTo(destination, bufferSize);

    public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) =>
        BaseStream.CopyToAsync(destination, bufferSize, cancellationToken);

    protected override void Dispose(bool disposing) {
        if (_refCounter is null)
            throw new ObjectDisposedException("Object already disposed");

        if (_refCounter.Release(out var leaveOpen) == 0 && !leaveOpen)
            BaseStream.Dispose();
        BaseStream = null!;
        _refCounter = null;
    }

    public override ValueTask DisposeAsync() {
        if (_refCounter is null)
            throw new ObjectDisposedException("Object already disposed");

        GC.SuppressFinalize(this);
        var disposeValueTask = _refCounter.Release(out var leaveOpen) == 0 && !leaveOpen
            ? ValueTask.CompletedTask
            : BaseStream.DisposeAsync();
        BaseStream = null!;
        _refCounter = null;
        return disposeValueTask;
    }

    public override void Flush() => BaseStream.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => BaseStream.FlushAsync(cancellationToken);

    public override IAsyncResult
        BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state) =>
        BaseStream.BeginRead(buffer, offset, count, callback, state);

    public override int EndRead(IAsyncResult asyncResult) => BaseStream.EndRead(asyncResult);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        BaseStream.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer,
        CancellationToken cancellationToken = new CancellationToken()) =>
        BaseStream.ReadAsync(buffer, cancellationToken);

    public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback,
        object? state) => BaseStream.BeginWrite(buffer, offset, count, callback, state);

    public override void EndWrite(IAsyncResult asyncResult) => BaseStream.EndWrite(asyncResult);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        BaseStream.WriteAsync(buffer, offset, count, cancellationToken);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = new CancellationToken()) =>
        BaseStream.WriteAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => BaseStream.Seek(offset, origin);

    public override void SetLength(long value) => BaseStream.SetLength(value);

    public override int Read(byte[] buffer, int offset, int count) => BaseStream.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => BaseStream.Read(buffer);

    public override int ReadByte() => BaseStream.ReadByte();

    public override void Write(byte[] buffer, int offset, int count) => BaseStream.Write(buffer, offset, count);

    public override void Write(ReadOnlySpan<byte> buffer) => BaseStream.Write(buffer);

    public override void WriteByte(byte value) => BaseStream.WriteByte(value);

    #endregion

    #region ICloneable

    public object Clone() => new StreamIStreamWrapper(this);

    #endregion

    #region IStream

    public void Clone(out IStream ppstm) => ppstm = new StreamIStreamWrapper(this);

    public void Commit(int grfCommitFlags) => throw new NotImplementedException();

    public unsafe void CopyTo(IStream pstm, long cbLong, nint pcbReadUntyped, nint pcbWrittenUntyped) {
        var totalRead = 0ul;
        var totalWritten = 0ul;
        var cb = unchecked((ulong) cbLong);

        var buffer = ArrayPool<byte>.Shared.Rent(unchecked((int) Math.Min(cb, 0x1000)));
        try {
            while (cb != totalRead) {
                var read = (ulong) BaseStream.Read(buffer, 0, unchecked((int) Math.Min(cb - totalRead, 0x1000)));
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

    public unsafe void Read(byte[] pv, int cb, nint pcbRead) {
        var offset = 0;
        while (offset != cb) {
            var read = BaseStream.Read(pv, offset, cb - offset);
            if (read == 0)
                break;
            offset += read;
        }

        if (pcbRead != 0)
            *(long*) pcbRead = offset;
    }

    public void Revert() => throw new NotImplementedException();

    public unsafe void Seek(long dlibMove, int dwOrigin, nint plibNewPosition) {
        var newPosition = BaseStream.Seek(dlibMove, (SeekOrigin) dwOrigin);
        if (plibNewPosition != 0)
            *(long*) plibNewPosition = newPosition;
    }

    public void SetSize(long libNewSize) => BaseStream.SetLength(libNewSize);

    public unsafe void Stat(out STATSTG pstatstg, int grfStatFlag) {
        pstatstg = new();
        switch (BaseStream) {
            case FileStream fs:
                pstatstg.pwcsName = fs.Name;
                try {
                    var fi = new FileInfo(fs.Name);
                    fixed (void* pmtime = &pstatstg.mtime)
                        *(long*)pmtime = fi.LastWriteTime.ToFileTime();
                    fixed (void* pctime = &pstatstg.ctime)
                        *(long*)pctime = fi.CreationTime.ToFileTime();
                    fixed (void* patime = &pstatstg.atime)
                        *(long*)patime = fi.LastAccessTime.ToFileTime();
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
            pstatstg.cbSize = BaseStream.Length;
        } catch (NotSupportedException) {
            throw new NotSupportedException();
        }
    }

    public void UnlockRegion(long libOffset, long cb, int dwLockType) => throw new NotImplementedException();

    public unsafe void Write(byte[] pv, int cb, nint pcbWritten) {
        BaseStream.Write(pv, 0, cb);
        if (pcbWritten != 0)
            *(long*) pcbWritten = cb;
    }

    #endregion

    private sealed class RefCounter {
        private readonly bool _leaveOpen;
        private int _ref = 1;

        public RefCounter(bool leaveOpen) {
            _leaveOpen = leaveOpen;
        }

        public int AddRef() => Interlocked.Increment(ref _ref);

        public int Release(out bool leaveOpen) {
            leaveOpen = _leaveOpen;
            return Interlocked.Decrement(ref _ref);
        }
    }
}
