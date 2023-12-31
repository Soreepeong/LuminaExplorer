using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Interop.Windows;
using IStreamTFX = TerraFX.Interop.Windows.IStream;
using IStream = System.Runtime.InteropServices.ComTypes.IStream;

namespace LuminaExplorer.Core.Util;

public static class StreamIStreamWrapper {
    public static StreamIStreamWrapper<T> WrapToIStream<T>(this T stream, bool leaveOpen = false) where T : Stream
        => new(stream, leaveOpen);
}

public partial class StreamIStreamWrapper<T> : ICloneable where T : Stream {
    private RefCounter? _refCounter;

    public StreamIStreamWrapper(T baseStream, bool leaveOpen = false) {
        BaseStream = baseStream;
        _refCounter = new(leaveOpen);
    }

    private StreamIStreamWrapper(StreamIStreamWrapper<T> cloneFrom) {
        ObjectDisposedException.ThrowIf(cloneFrom._refCounter is null, this);

        _refCounter = cloneFrom._refCounter;
        _refCounter.AddRef();
        BaseStream = cloneFrom.BaseStream;
    }
    
    public T BaseStream { get; set; }

    public unsafe ComPtr<IStreamTFX> CreateNativeRef() =>
        (IStreamTFX*) Marshal.GetComInterfaceForObject(this, typeof(IStream));

    public object Clone() => new StreamIStreamWrapper<T>(this);

    public override ValueTask DisposeAsync() {
        ObjectDisposedException.ThrowIf(_refCounter is null, this);

        GC.SuppressFinalize(this);
        var disposeValueTask = _refCounter.Release(out var leaveOpen) == 0 && !leaveOpen
            ? ValueTask.CompletedTask
            : BaseStream.DisposeAsync();
        BaseStream = null!;
        _refCounter = null;
        return disposeValueTask;
    }

    protected override void Dispose(bool disposing) {
        if (_refCounter is null)
            throw new ObjectDisposedException("Object already disposed");

        if (_refCounter.Release(out var leaveOpen) == 0 && !leaveOpen)
            BaseStream.Dispose();
        BaseStream = null!;
        _refCounter = null;
    }

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
