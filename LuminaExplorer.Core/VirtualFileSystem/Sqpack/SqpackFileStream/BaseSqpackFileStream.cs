using System;
using System.IO;
using Lumina.Data;
using Lumina.Data.Structs;

namespace LuminaExplorer.Core.VirtualFileSystem.Sqpack.SqpackFileStream;

public abstract class BaseSqpackFileStream : Stream, ICloneable {
    protected uint PositionUint;

    public readonly PlatformId PlatformId;

    protected BaseSqpackFileStream(PlatformId platformId, uint length)
    {
        this.PlatformId = platformId;
        this.Length = length;
    }

    public override void Flush()
    { }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var newPosition = origin switch {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => this.Position + offset,
            SeekOrigin.End => this.Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null),
        };
        if (newPosition < 0 || newPosition > this.Length)
            throw new IOException();
        return this.PositionUint = (uint) newPosition;
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count) =>
        this.ReadAsync(buffer, offset, count, default).GetAwaiter().GetResult();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length { get; }

    public override long Position {
        get => this.PositionUint;
        set => this.Seek(value, SeekOrigin.Begin);
    }

    protected int ReadImplPadTo(byte[] buffer, ref int offset, ref int count, uint padTo)
    {
        var pad = (int) Math.Min(padTo - this.PositionUint, count);
        Array.Fill(buffer, (byte) 0, offset, pad);
        offset += pad;
        count -= pad;
        this.PositionUint += (uint) pad;
        return pad;
    }

    public object Clone() => this.Clone(false);

    public virtual void CloseButOpenAgainWhenNecessary()
    { }

    public abstract BaseSqpackFileStream Clone(bool keepOpen);

    protected class BaseOffsetManager {
        private readonly string _datPath;
        private readonly PlatformId _platformId;
        public readonly long BaseOffset;

        public BaseOffsetManager(string datPath, PlatformId platformId, long baseOffset)
        {
            this._datPath = datPath;
            this._platformId = platformId;
            this.BaseOffset = baseOffset;
        }

        public LuminaBinaryReader CreateNewReader() => new(File.OpenRead(this._datPath), this._platformId);
    }
}
