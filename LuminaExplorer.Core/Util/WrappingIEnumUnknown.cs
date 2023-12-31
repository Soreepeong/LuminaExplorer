using System.Collections;
using System.Collections.Generic;
using TerraFX.Interop.Windows;

namespace LuminaExplorer.Core.Util;

public struct WrappingIEnumUnknown<T> : IEnumerable<ComPtr<T>>, IEnumerator<ComPtr<T>>
    where T : unmanaged, IUnknown.Interface {
    private ComPtr<IEnumUnknown> _enumerator;
    private ComPtr<T> _current;

    public unsafe WrappingIEnumUnknown(IEnumUnknown* enumerator) {
        _enumerator.Attach(enumerator);
        _enumerator.Get()->AddRef();
    }

    public ComPtr<T> Current => _current;

    object IEnumerator.Current => Current;

    public unsafe bool MoveNext() {
        while (true) {
            using var unk = default(ComPtr<IUnknown>);
            var fetched = 0u;
            _enumerator.Get()->Next(1, unk.GetAddressOf(), &fetched).Throw();
            if (fetched == 0)
                return false;

            fixed (ComPtr<T>* pCurrent = &_current) {
                if (unk.CopyTo(pCurrent).SUCCEEDED)
                    return true;
            }
        }
    }

    public void Reset() => _enumerator.Reset();

    public void Dispose() {
        _enumerator.Reset();
        _current.Reset();
    }

    public IEnumerator<ComPtr<T>> GetEnumerator() => this;

    IEnumerator IEnumerable.GetEnumerator() => this;
}
