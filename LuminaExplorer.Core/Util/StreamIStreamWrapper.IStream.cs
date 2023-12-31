using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices.ComTypes;

namespace LuminaExplorer.Core.Util;

public partial class StreamIStreamWrapper<T> : IStream where T : Stream {
    public void Clone(out IStream ppstm) => ppstm = new StreamIStreamWrapper<T>(this);

    public void Commit(int grfCommitFlags) => throw new NotSupportedException();

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

    public void LockRegion(long libOffset, long cb, int dwLockType) => throw new NotSupportedException();

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

    public void Revert() => throw new NotSupportedException();

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

    public void UnlockRegion(long libOffset, long cb, int dwLockType) => throw new NotSupportedException();

    public unsafe void Write(byte[] pv, int cb, nint pcbWritten) {
        BaseStream.Write(pv, 0, cb);
        if (pcbWritten != 0)
            *(long*) pcbWritten = cb;
    }
}